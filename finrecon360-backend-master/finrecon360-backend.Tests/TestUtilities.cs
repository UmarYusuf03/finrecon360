using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Options;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace finrecon360_backend.Tests;

public class FakeEmailSender : IEmailSender
{
    public List<EmailRequest> Requests { get; } = new();

    public Task SendTemplateAsync(string toEmail, long templateId, IDictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        Requests.Add(new EmailRequest(toEmail, templateId, new Dictionary<string, object>(parameters)));
        return Task.CompletedTask;
    }
}

public record EmailRequest(string ToEmail, long TemplateId, IDictionary<string, object> Parameters);

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeEmailSender EmailSender { get; } = new();
    private readonly string _dbName = $"TestDb-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-should-be-long-32chars",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:ExpiresMinutes"] = "60",
                ["FRONTEND_BASE_URL"] = "http://localhost:4200",
                ["BREVO_TEMPLATE_ID_MAGICLINK_VERIFY"] = "1",
                ["BREVO_TEMPLATE_ID_MAGICLINK_INVITE"] = "4",
                ["BREVO_TEMPLATE_ID_MAGICLINK_RESET"] = "2",
                ["BREVO_TEMPLATE_ID_MAGICLINK_CHANGE"] = "3"
            };
            config.AddInMemoryCollection(settings);
        });


        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            services.RemoveAll(typeof(IEmailSender));
            services.AddSingleton<IEmailSender>(EmailSender);

            
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = "test-issuer",
                    ValidAudience = "test-audience",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-should-be-long-32chars")),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
services.Configure<JwtSettings>(options =>
            {
                options.Key = "test-signing-key-should-be-long-32chars";
                options.Issuer = "test-issuer";
                options.Audience = "test-audience";
                options.ExpiresMinutes = 60;
            });

            services.Configure<BrevoOptions>(options =>
            {
                options.ApiKey = "test";
                options.SenderEmail = "sender@test.local";
                options.SenderName = "Test";
                options.TemplateIdMagicLinkVerify = 1;
                options.TemplateIdMagicLinkInvite = 4;
                options.TemplateIdMagicLinkReset = 2;
                options.TemplateIdMagicLinkChange = 3;
            });

            services.Configure<MagicLinkOptions>(options =>
            {
                options.ExpiresMinutes = 10;
                options.MaxAttempts = 3;
                options.ResendCooldownSeconds = 0;
                options.FrontendBaseUrl = "http://localhost:4200";
            });
        });
    }

    public static HttpClient CreateAuthenticatedClient(TestWebApplicationFactory factory, Guid userId, string email)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-should-be-long-32chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "test-issuer",
            audience: "test-audience",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }
}
