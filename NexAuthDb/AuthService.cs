// ============================================================
//  NexAuth — Application: AuthService (all auth flows)
// ============================================================
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexAuth.Application.Auth.DTOs;
using NexAuth.Application.Auth.Services;
using NexAuth.Domain.Entities;
using NexAuth.Infrastructure.Persistence;
using NexAuth.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace NexAuth.Application.Auth
{
    public class AuthService : IAuthService
    {
        private static readonly Guid DefaultRoleId = new("00000000-0000-0000-0000-000000000001");

        private readonly NexAuthDbContext _db;
        private readonly IJwtService _jwt;
        private readonly IGoogleAuthService _google;
        private readonly IEmailService _email;
        private readonly IPasswordHasher<User> _hasher;
        private readonly IConfiguration _config;

        public AuthService(
            NexAuthDbContext db,
            IJwtService jwt,
            IGoogleAuthService google,
            IEmailService email,
            IPasswordHasher<User> hasher,
            IConfiguration config)
        {
            _db = db;
            _jwt = jwt;
            _google = google;
            _email = email;
            _hasher = hasher;
            _config = config;
        }

        // ── Register ─────────────────────────────────────────────
        public async Task<ServiceResult<AuthResponse>> RegisterAsync(
            RegisterRequest req, string ip, string ua, CancellationToken ct = default)
        {
            var existingUser = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant(), ct);

            if (existingUser is not null)
                return ServiceResult<AuthResponse>.Fail("Email already in use.", 409);

            var user = new User
            {
                Email = req.Email.ToLowerInvariant(),
                FullName = req.FullName.Trim(),
            };
            user.PasswordHash = _hasher.HashPassword(user, req.Password);

            _db.Users.Add(user);
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = DefaultRoleId });

            await _db.SaveChangesAsync(ct);
            await AuditAsync(user.Id, user.Email, "Email", ip, ua, true, null, ct);

            return await BuildAuthResponseAsync(user, ip, ua, ct);
        }

        // ── Email/Password Login ──────────────────────────────────
        public async Task<ServiceResult<AuthResponse>> LoginAsync(
            LoginRequest req, string ip, string ua, CancellationToken ct = default)
        {
            var user = await _db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant(), ct);

            if (user is null || !user.IsActive)
            {
                await AuditAsync(null, req.Email, "Email", ip, ua, false, "User not found or inactive", ct);
                return ServiceResult<AuthResponse>.Fail("Invalid credentials.", 401);
            }

            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash ?? "", req.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                await AuditAsync(user.Id, user.Email, "Email", ip, ua, false, "Wrong password", ct);
                return ServiceResult<AuthResponse>.Fail("Invalid credentials.", 401);
            }

            // Rehash if needed (BCrypt upgraded)
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _hasher.HashPassword(user, req.Password);
                await _db.SaveChangesAsync(ct);
            }

            await AuditAsync(user.Id, user.Email, "Email", ip, ua, true, null, ct);
            return await BuildAuthResponseAsync(user, ip, ua, ct);
        }

        // ── Google OAuth Login ────────────────────────────────────
        public async Task<ServiceResult<AuthResponse>> GoogleLoginAsync(
            GoogleLoginRequest req, string ip, string ua, CancellationToken ct = default)
        {
            var googleUser = await _google.ValidateIdTokenAsync(req.IdToken, ct);
            if (googleUser is null)
                return ServiceResult<AuthResponse>.Fail("Invalid Google token.", 401);

            // Check if provider mapping already exists
            var extLogin = await _db.ExternalLogins
                .Include(e => e.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(e => e.Provider == "Google" && e.ProviderKey == googleUser.Sub, ct);

            User user;

            if (extLogin is not null)
            {
                user = extLogin.User;
                if (!user.IsActive)
                {
                    await AuditAsync(user.Id, user.Email, "Google", ip, ua, false, "Account inactive", ct);
                    return ServiceResult<AuthResponse>.Fail("Account is inactive.", 403);
                }
            }
            else
            {
                // Auto-register: try to link by email first
                user = await _db.Users
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Email == googleUser.Email.ToLowerInvariant(), ct)
                    ?? CreateNewUserFromGoogle(googleUser);

                if (_db.Entry(user).State == EntityState.Detached)
                    _db.Users.Add(user);

                _db.ExternalLogins.Add(new ExternalLogin
                {
                    UserId = user.Id,
                    Provider = "Google",
                    ProviderKey = googleUser.Sub,
                    ProviderDisplayName = googleUser.Name,
                });

                if (!user.UserRoles.Any())
                    _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = DefaultRoleId });

                await _db.SaveChangesAsync(ct);
            }

            await AuditAsync(user.Id, user.Email, "Google", ip, ua, true, null, ct);
            return await BuildAuthResponseAsync(user, ip, ua, ct);
        }

        // ── Refresh Token ─────────────────────────────────────────
        public async Task<ServiceResult<AuthResponse>> RefreshTokenAsync(
            RefreshTokenRequest req, CancellationToken ct = default)
        {
            var stored = await _db.RefreshTokens
                .Include(r => r.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(r => r.Token == req.RefreshToken, ct);

            if (stored is null || !stored.IsActive)
                return ServiceResult<AuthResponse>.Fail("Invalid or expired refresh token.", 401);

            // Rotation: revoke old, issue new
            stored.IsRevoked = true;
            stored.RevokedAt = DateTime.UtcNow;

            var newRefresh = IssueRefreshToken(stored.UserId);
            stored.ReplacedByToken = newRefresh.Token;

            _db.RefreshTokens.Add(newRefresh);
            await _db.SaveChangesAsync(ct);

            var roles = stored.User.UserRoles.Select(ur => ur.Role.Name).ToList();
            var accessToken = _jwt.GenerateAccessToken(stored.User, roles);

            return ServiceResult<AuthResponse>.Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefresh.Token,
                AccessTokenExpiresAt = _jwt.GetAccessTokenExpiry(),
                User = MapUser(stored.User, roles),
            });
        }

        // ── Logout ────────────────────────────────────────────────
        public async Task<ServiceResult<MessageResponse>> LogoutAsync(
            LogoutRequest req, CancellationToken ct = default)
        {
            var token = await _db.RefreshTokens
                .FirstOrDefaultAsync(r => r.Token == req.RefreshToken, ct);

            if (token is not null && token.IsActive)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            return ServiceResult<MessageResponse>.Ok(new MessageResponse("Logged out."));
        }

        // ── Forgot Password ───────────────────────────────────────
        public async Task<ServiceResult<MessageResponse>> ForgotPasswordAsync(
            ForgotPasswordRequest req, CancellationToken ct = default)
        {
            // Always return success to prevent email enumeration
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant(), ct);

            if (user is not null)
            {
                var token = GenerateSecureToken();
                _db.PasswordResets.Add(new PasswordReset
                {
                    UserId = user.Id,
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                });
                await _db.SaveChangesAsync(ct);

                var frontendUrl = _config["Frontend:BaseUrl"] ?? "https://app.nexauth.com";
                var link = $"{frontendUrl}/reset-password?token={Uri.EscapeDataString(token)}";
                await _email.SendPasswordResetEmailAsync(user.Email, link, ct);
            }

            return ServiceResult<MessageResponse>.Ok(
                new MessageResponse("If that email exists, a reset link was sent."));
        }

        // ── Reset Password ────────────────────────────────────────
        public async Task<ServiceResult<MessageResponse>> ResetPasswordAsync(
            ResetPasswordRequest req, CancellationToken ct = default)
        {
            var reset = await _db.PasswordResets
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Token == req.Token, ct);

            if (reset is null || !reset.IsValid)
                return ServiceResult<MessageResponse>.Fail("Token is invalid or has expired.", 400);

            reset.User.PasswordHash = _hasher.HashPassword(reset.User, req.NewPassword);
            reset.User.UpdatedAt = DateTime.UtcNow;
            reset.IsUsed = true;
            reset.UsedAt = DateTime.UtcNow;

            // Revoke all active refresh tokens for security
            var activeTokens = await _db.RefreshTokens
                .Where(t => t.UserId == reset.UserId && !t.IsRevoked)
                .ToListAsync(ct);
            foreach (var t in activeTokens) { t.IsRevoked = true; t.RevokedAt = DateTime.UtcNow; }

            await _db.SaveChangesAsync(ct);

            return ServiceResult<MessageResponse>.Ok(new MessageResponse("Password updated successfully."));
        }

        // ── Private helpers ───────────────────────────────────────

        private async Task<ServiceResult<AuthResponse>> BuildAuthResponseAsync(
            User user, string ip, string ua, CancellationToken ct)
        {
            // Re-load roles if not already loaded
            if (!user.UserRoles.Any())
                await _db.Entry(user).Collection(u => u.UserRoles).Query()
                    .Include(ur => ur.Role).LoadAsync(ct);

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            var accessToken = _jwt.GenerateAccessToken(user, roles);
            var refreshToken = IssueRefreshToken(user.Id);

            _db.RefreshTokens.Add(refreshToken);
            await _db.SaveChangesAsync(ct);

            return ServiceResult<AuthResponse>.Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                AccessTokenExpiresAt = _jwt.GetAccessTokenExpiry(),
                User = MapUser(user, roles),
            });
        }

        private RefreshToken IssueRefreshToken(Guid userId) => new()
        {
            UserId = userId,
            Token = _jwt.GenerateRefreshToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };

        private static User CreateNewUserFromGoogle(GoogleUserInfo g) => new()
        {
            Email = g.Email.ToLowerInvariant(),
            FullName = g.Name,
            AvatarUrl = g.Picture,
            PasswordHash = null,   // OAuth user — no local password
        };

        private static UserDto MapUser(User user, IEnumerable<string> roles) => new()
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            AvatarUrl = user.AvatarUrl,
            Roles = roles,
        };

        private static string GenerateSecureToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private async Task AuditAsync(
            Guid? userId, string? email, string method,
            string ip, string ua, bool success, string? reason,
            CancellationToken ct)
        {
            _db.LoginAuditLogs.Add(new LoginAuditLog
            {
                UserId = userId,
                Email = email,
                Method = method,
                IpAddress = ip,
                UserAgent = ua,
                IsSuccess = success,
                FailReason = reason,
            });
            await _db.SaveChangesAsync(ct);
        }
    }
}
