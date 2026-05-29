// ============================================================
//  NexAuth — Application Layer
//  DTOs, Command/Result records, Service interfaces
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NexAuth.Application.Auth.DTOs
{
    // ── Request DTOs ────────────────────────────────────────

    public record RegisterRequest
    {
        [Required, EmailAddress]
        public string Email { get; init; } = string.Empty;

        [Required, MinLength(2)]
        public string FullName { get; init; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; init; } = string.Empty;
    }

    public record LoginRequest
    {
        [Required, EmailAddress]
        public string Email { get; init; } = string.Empty;

        [Required]
        public string Password { get; init; } = string.Empty;
    }

    public record GoogleLoginRequest
    {
        [Required]
        public string IdToken { get; init; } = string.Empty;   // from @react-oauth/google
    }

    public record RefreshTokenRequest
    {
        [Required]
        public string RefreshToken { get; init; } = string.Empty;
    }

    public record ForgotPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; init; } = string.Empty;
    }

    public record ResetPasswordRequest
    {
        [Required]
        public string Token { get; init; } = string.Empty;

        [Required, MinLength(8)]
        public string NewPassword { get; init; } = string.Empty;
    }

    public record LogoutRequest
    {
        [Required]
        public string RefreshToken { get; init; } = string.Empty;
    }

    // ── Response DTOs ───────────────────────────────────────

    public record AuthResponse
    {
        public string AccessToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; init; }
        public UserDto User { get; init; } = null!;
    }

    public record UserDto
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string? AvatarUrl { get; init; }
        public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    }

    public record MessageResponse(string Message);

    // ── Service Result wrapper (avoids exceptions for logic errors) ─

    public class ServiceResult<T>
    {
        public bool Succeeded { get; private set; }
        public T? Data { get; private set; }
        public string? Error { get; private set; }
        public int StatusCode { get; private set; }

        public static ServiceResult<T> Ok(T data) =>
            new() { Succeeded = true, Data = data, StatusCode = 200 };

        public static ServiceResult<T> Fail(string error, int code = 400) =>
            new() { Succeeded = false, Error = error, StatusCode = code };
    }
}

// ── Service interfaces ──────────────────────────────────────
namespace NexAuth.Application.Auth.Services
{
    using NexAuth.Application.Auth.DTOs;
    using System.Threading;

    public interface IAuthService
    {
        Task<ServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request, string ip, string userAgent, CancellationToken ct = default);
        Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request, string ip, string userAgent, CancellationToken ct = default);
        Task<ServiceResult<AuthResponse>> GoogleLoginAsync(GoogleLoginRequest request, string ip, string userAgent, CancellationToken ct = default);
        Task<ServiceResult<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
        Task<ServiceResult<MessageResponse>> LogoutAsync(LogoutRequest request, CancellationToken ct = default);
        Task<ServiceResult<MessageResponse>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
        Task<ServiceResult<MessageResponse>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    }

    public interface IJwtService
    {
        string GenerateAccessToken(NexAuth.Domain.Entities.User user, IEnumerable<string> roles);
        string GenerateRefreshToken();
        DateTime GetAccessTokenExpiry();
    }

    public interface IGoogleAuthService
    {
        Task<GoogleUserInfo?> ValidateIdTokenAsync(string idToken, CancellationToken ct = default);
    }

    public record GoogleUserInfo(string Sub, string Email, string Name, string? Picture);

    public interface IEmailService
    {
        Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct = default);
    }
}
