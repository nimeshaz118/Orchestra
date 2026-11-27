
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Orchestra.Api.Tests;

public sealed class ResendVerificationTests
{
    private const string Url =
        "/api/v1/auth/resend-verification";

    // TEST 1: Unknown account receives a neutral response.
    [Fact]
    public async Task Unknown_email_returns_neutral_response()
    {
        await using var host = new ApiHost();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url, new { email = "unknown@example.test" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(host.Email.Sent);
    }

    // TEST 2: Already verified accounts receive no new code.
    [Fact]
    public async Task Verified_account_receives_no_new_code()
    {
        await using var host = new ApiHost();
        await host.SeedUserAsync(
            "verified@example.test", "USER");

        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url, new { email = "verified@example.test" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(host.Email.Sent);
    }

    // TEST 3: Invalid email input is rejected.
    [Theory]
    [InlineData("")]
    [InlineData("invalid-email")]
    [InlineData("missing-at-symbol")]
    public async Task Invalid_email_returns_422(string email)
    {
        await using var host = new ApiHost();
        var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url, new { email });

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            response.StatusCode);
    }

    // TEST 4: Resending within 60 seconds is rejected.
    [Fact]
    public async Task Resend_within_cooldown_returns_429()
    {
        await using var host = new ApiHost();

        var department = await host.SeedDepartmentAsync(
            "Engineering", "ACTIVE");

        var client = host.CreateClient();

        // Register a new pending account.
        var register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email = "pending@example.test",
                password = "long-passphrase-123",
                firstName = "Pending",
                lastName = "User",
                departmentId = department
            });

        Assert.Equal(
            HttpStatusCode.Created,
            register.StatusCode);

        Assert.Single(host.Email.Sent);

        // Immediately request another verification code.
        var resend = await client.PostAsJsonAsync(
            Url,
            new { email = "pending@example.test" });

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            resend.StatusCode);

        // No additional email should have been sent.
        Assert.Single(host.Email.Sent);
    }
}
