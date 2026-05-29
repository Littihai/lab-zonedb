// ============================================================
//  NexAuth — Infrastructure: JWT + Google Auth + Email services
// ============================================================
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NexAuth.Application.Auth.Services;
using NexAuth.Domain.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace NexAuth.Infrastructure.Services
{
    // ════════════════════════════════════════════════════════
    //  JWT
    // ════════════════════════════════════════════════════════
    public class JwtOptions
    {
        public const string Section = "Jwt";
        public string SecretKey { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int AccessTokenMinutes { get; set; } = 20;
        public int RefreshTokenDays { get; set; } = 7;
    }

    public class JwtService : IJwtService
    {
        private readonly JwtOptions _opts;
        public JwtService(IOptions<JwtOptions> opts) => _opts = opts.Value;

        public string GenerateAccessToken(User user, IEnumerable<string> roles)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SecretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new(JwtRegisteredClaimNames.Name,  user.FullName),
                new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            };
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var token = new JwtSecurityToken(
                issuer: _opts.Issuer,
                audience: _opts.Audience,
                claims: claims,
                expires: GetAccessTokenExpiry(),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            var bytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public DateTime GetAccessTokenExpiry() =>
            DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);
    }

    // ════════════════════════════════════════════════════════
    //  Google OAuth
    // ════════════════════════════════════════════════════════
    public class GoogleAuthOptions
    {
        public const string Section = "Google";
        public string ClientId { get; set; } = string.Empty;
    }

    public class GoogleAuthService : IGoogleAuthService
    {
        private readonly GoogleAuthOptions _opts;
        private readonly HttpClient _http;
        private readonly ILogger<GoogleAuthService> _log;

        public GoogleAuthService(
            IOptions<GoogleAuthOptions> opts,
            HttpClient http,
            ILogger<GoogleAuthService> log)
        {
            _opts = opts.Value;
            _http = http;
            _log = log;
        }

        public async Task<GoogleUserInfo?> ValidateIdTokenAsync(
            string idToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(idToken))
            {
                _log.LogWarning("[Google] idToken is null or empty");
                return null;
            }

            _log.LogInformation("[Google] Validating token length={Len}", idToken.Length);

            HttpResponseMessage resp;
            string rawJson;
            try
            {
                resp = await _http.GetAsync(
                    $"https://oauth2.googleapis.com/tokeninfo?id_token={idToken}", ct);
                rawJson = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Google] HTTP call failed");
                return null;
            }

            _log.LogInformation("[Google] status={Status} body={Body}",
                (int)resp.StatusCode, rawJson);

            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // ── Validate aud ──────────────────────────────────
            if (!root.TryGetProperty("aud", out var audProp))
            {
                _log.LogWarning("[Google] 'aud' missing");
                return null;
            }

            var audValue = audProp.GetString() ?? string.Empty;
            if (!audValue.Equals(_opts.ClientId, StringComparison.Ordinal))
            {
                _log.LogWarning("[Google] aud mismatch aud={Aud} config={Cfg}",
                    audValue, _opts.ClientId);
                return null;
            }

            // ── Validate exp ──────────────────────────────────
            if (root.TryGetProperty("exp", out var expProp))
            {
                long expSec = expProp.ValueKind == JsonValueKind.Number
                    ? expProp.GetInt64()
                    : long.Parse(expProp.GetString() ?? "0");

                if (DateTimeOffset.FromUnixTimeSeconds(expSec) < DateTimeOffset.UtcNow)
                {
                    _log.LogWarning("[Google] Token expired");
                    return null;
                }
            }

            // ── Extract claims ────────────────────────────────
            var sub = root.TryGetProperty("sub", out var s) ? s.GetString() ?? "" : "";
            var email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var picture = root.TryGetProperty("picture", out var p) ? p.GetString() : null;

            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email))
            {
                _log.LogWarning("[Google] sub/email missing");
                return null;
            }

            _log.LogInformation("[Google] OK sub={Sub} email={Email}", sub, email);
            return new GoogleUserInfo(sub, email, name, picture);
        }
    }

    // ════════════════════════════════════════════════════════
    //  Email
    // ════════════════════════════════════════════════════════
    public class EmailOptions
    {
        public const string Section = "Email";
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = "NexAuth";
    }

    public class EmailService : IEmailService
    {
        private readonly EmailOptions _opts;
        private readonly ILogger<EmailService> _log;

        public EmailService(IOptions<EmailOptions> opts, ILogger<EmailService> log)
        {
            _opts = opts.Value;
            _log = log;
        }

        public async Task SendPasswordResetEmailAsync(
            string toEmail, string resetLink, CancellationToken ct = default)
        {
            // DEV: log only — swap with MailKit/SendGrid for production
            _log.LogInformation("[Email] Reset link for {Email}: {Link}", toEmail, resetLink);
            await Task.CompletedTask;
        }
    }
}