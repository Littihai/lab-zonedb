// ============================================================
//  NexAuth — WebAPI: AuthController
// ============================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexAuth.Application.Auth;
using NexAuth.Application.Auth.DTOs;
using NexAuth.Application.Auth.Services;

namespace NexAuth.WebAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;

        public AuthController(IAuthService auth) => _auth = auth;

        private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        private string Ua => Request.Headers.UserAgent.ToString();

        // POST /api/auth/register
        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(AuthResponse), 201)]
        [ProducesResponseType(typeof(ProblemDetails), 409)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
        {
            var result = await _auth.RegisterAsync(req, Ip, Ua, ct);
            return result.Succeeded
                ? StatusCode(201, result.Data)
                : Problem(result.Error, statusCode: result.StatusCode);
        }

        // POST /api/auth/login
        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 401)]
        public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
        {
            var result = await _auth.LoginAsync(req, Ip, Ua, ct);
            return result.Succeeded ? Ok(result.Data) : Problem(result.Error, statusCode: result.StatusCode);
        }

        // POST /api/auth/google-login
        [HttpPost("google-login")]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 401)]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest req, CancellationToken ct)
        {
            var result = await _auth.GoogleLoginAsync(req, Ip, Ua, ct);
            return result.Succeeded ? Ok(result.Data) : Problem(result.Error, statusCode: result.StatusCode);
        }

        // POST /api/auth/refresh
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 401)]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req, CancellationToken ct)
        {
            var result = await _auth.RefreshTokenAsync(req, ct);
            return result.Succeeded ? Ok(result.Data) : Problem(result.Error, statusCode: result.StatusCode);
        }

        // POST /api/auth/logout
        [HttpPost("logout")]
        [ProducesResponseType(typeof(MessageResponse), 200)]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest req, CancellationToken ct)
        {
            var result = await _auth.LogoutAsync(req, ct);
            return Ok(result.Data);
        }

        // POST /api/auth/forgot-password
        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(MessageResponse), 200)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
        {
            var result = await _auth.ForgotPasswordAsync(req, ct);
            return Ok(result.Data); // Always 200 — prevent email enumeration
        }

        // POST /api/auth/reset-password
        [HttpPost("reset-password")]
        [ProducesResponseType(typeof(MessageResponse), 200)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
        {
            var result = await _auth.ResetPasswordAsync(req, ct);
            return result.Succeeded ? Ok(result.Data) : Problem(result.Error, statusCode: result.StatusCode);
        }

        // ── Problem helper ────────────────────────────────────────
        private ObjectResult Problem(string? detail, int statusCode = 400) =>
            StatusCode(statusCode, new ProblemDetails
            {
                Title = statusCode switch
                {
                    400 => "Bad Request",
                    401 => "Unauthorized",
                    403 => "Forbidden",
                    409 => "Conflict",
                    _ => "Error"
                },
                Detail = detail,
                Status = statusCode,
            });
    }
}