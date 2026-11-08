using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Orchestra.Api.Modules.Auth;

public interface IEmailSender
{
    Task SendCodeAsync(string accountEmail, string purpose, string code, CancellationToken cancellationToken);
}

public sealed class ResendEmailSender(HttpClient client, IConfiguration configuration,
    IWebHostEnvironment environment) : IEmailSender
{
    public async Task SendCodeAsync(string accountEmail, string purpose, string code,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Resend:ApiKey"];
        var from = configuration["Resend:From"];
        var overrideRecipient = environment.IsDevelopment()
            ? configuration["DevelopmentEmail:Recipient"] : null;
        var recipient = string.IsNullOrWhiteSpace(overrideRecipient) ? accountEmail : overrideRecipient;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Configure Resend:ApiKey and Resend:From in User Secrets.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            from, to = new[] { recipient },
            subject = $"Orchestra {purpose} code for {accountEmail}",
            text = $"Account: {accountEmail}\nYour Orchestra {purpose} code is {code}. It expires in 10 minutes."
        });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
