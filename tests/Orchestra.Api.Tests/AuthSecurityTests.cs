using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Auth;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class AuthSecurityTests
{
    private readonly AuthSettings _settings = new()
    {
        SigningKey = new string('s', 48), CodeHashKey = new string('c', 48)
    };

    [Fact]
    public void Verification_codes_are_bound_to_user_and_purpose()
    {
        var codes = new CodeService(_settings);
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        var hash = codes.Hash(user, "EMAIL_VERIFICATION", "001234");
        Assert.True(codes.Matches(user, "EMAIL_VERIFICATION", "001234", hash));
        Assert.False(codes.Matches(other, "EMAIL_VERIFICATION", "001234", hash));
        Assert.False(codes.Matches(user, "PASSWORD_RESET", "001234", hash));
        Assert.False(codes.Matches(user, "EMAIL_VERIFICATION", "001235", hash));
    }

    [Fact]
    public void Reset_token_cannot_be_replaced_by_access_token()
    {
        var tokens = new TokenService(_settings);
        var user = new AppUser
        {
            UserId = Guid.NewGuid(), Email = "test@example.test", FirstName = "Test",
            LastName = "User", RoleCode = "USER", Status = "ACTIVE", SecurityStamp = Guid.NewGuid()
        };
        var challenge = Guid.NewGuid();
        var reset = tokens.ResetToken(user, challenge);
        Assert.Equal(challenge, tokens.ValidateResetToken(reset)?.ChallengeId);
        Assert.Null(tokens.ValidateResetToken(tokens.AccessToken(user).AccessToken));
    }

    [Theory]
    [InlineData("Development", "tester@example.test")]
    [InlineData("Production", "account@example.test")]
    public async Task Recipient_override_is_limited_to_development(string environment, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resend:ApiKey"] = "test-key", ["Resend:From"] = "onboarding@resend.dev",
            ["DevelopmentEmail:Recipient"] = "tester@example.test"
        }).Build();
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(client, config, new TestEnvironment(environment));
        await sender.SendCodeAsync("account@example.test", "verification", "001234", default);
        Assert.Equal(expected, handler.Recipient);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Recipient { get; private set; }
        public string? AuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Recipient = json.RootElement.GetProperty("to")[0].GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"test\"}")
            };
        }
    }

    private sealed class TestEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Orchestra.Api.Tests";
        public string WebRootPath { get; set; } = ".";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
