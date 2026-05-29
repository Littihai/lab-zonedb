using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NexAuth.Application.Auth;
using NexAuth.Application.Auth.Services;
using NexAuth.Domain.Entities;
using NexAuth.Infrastructure.Persistence;
using NexAuth.Infrastructure.Services;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
// 1. Database
// ─────────────────────────────────────────────────────────────
builder.Services.AddDbContext<NexAuthDbContext>(opts =>
    opts.UseNpgsql(
        builder.Configuration.GetConnectionString("NexAuthDb"),
        npgsql => npgsql.EnableRetryOnFailure()
    ));

// ─────────────────────────────────────────────────────────────
// 2. JWT Authentication
// ─────────────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection(JwtOptions.Section);

builder.Services.Configure<JwtOptions>(jwtSection);

var jwtKey = jwtSection.GetValue<string>("SecretKey");

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new Exception("JWT SecretKey is missing.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtSection.GetValue<string>("Issuer"),
            ValidAudience = jwtSection.GetValue<string>("Audience"),

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminOnly",
        p => p.RequireRole("Admin"));
});

// ─────────────────────────────────────────────────────────────
// 3. Rate Limiting
// ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(opts =>
{
    opts.AddFixedWindowLimiter("auth", lim =>
    {
        lim.PermitLimit = 10;
        lim.Window = TimeSpan.FromMinutes(1);
        lim.QueueLimit = 0;
        lim.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    opts.RejectionStatusCode = 429;
});

// ─────────────────────────────────────────────────────────────
// 4. CORS
// ─────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(opts =>
{
    opts.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ─────────────────────────────────────────────────────────────
// 5. Services
// ─────────────────────────────────────────────────────────────
builder.Services.Configure<GoogleAuthOptions>(
    builder.Configuration.GetSection(GoogleAuthOptions.Section));

builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.Section));

builder.Services.AddHttpClient<IGoogleAuthService, GoogleAuthService>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddScoped<IPasswordHasher<User>,
    PasswordHasher<User>>();

// ─────────────────────────────────────────────────────────────
// 6. Controllers + Swagger
// ─────────────────────────────────────────────────────────────
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NexAuth API",
        Version = "v1",
        Description = "NexAuth Authentication API"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT Token"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ─────────────────────────────────────────────────────────────
// 7. Database Connection Check
// ─────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<NexAuthDbContext>();

    try
    {
        var ok = await db.Database.CanConnectAsync();

        Console.WriteLine($"[DB] Connected: {ok}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] Connection failed: {ex.Message}");
    }
}

// ─────────────────────────────────────────────────────────────
// 8. Swagger
// ─────────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "NexAuth API v1");
});

// ─────────────────────────────────────────────────────────────
// 9. Middleware Pipeline
// ─────────────────────────────────────────────────────────────

// IMPORTANT:
// Render handles HTTPS automatically
// so do NOT use app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

// ─────────────────────────────────────────────────────────────
// 10. Run App
// ─────────────────────────────────────────────────────────────
app.Run();
