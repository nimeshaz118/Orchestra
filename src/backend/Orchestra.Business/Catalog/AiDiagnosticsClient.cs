using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Orchestra.Api.Common;
using Orchestra.Api.Modules.Models;
using Orchestra.Api.Modules.Providers;

namespace Orchestra.Api.Modules.Catalog;

public sealed class AiDiagnosticsClient(HttpClient client, IConfiguration configuration)
{
    public async Task<CatalogTestResult> TestAsync(
        ProviderDto provider, ModelDto? model, CancellationToken ct)
    {
        var serviceToken = configuration["AiService:ServiceToken"];
        if (string.IsNullOrWhiteSpace(serviceToken) ||
            !Uri.TryCreate(configuration["AiService:BaseUrl"], UriKind.Absolute, out var serviceUrl) ||
            serviceUrl.Scheme is not ("http" or "https") ||
            serviceUrl.UserInfo.Length != 0 || serviceUrl.Query.Length != 0 || serviceUrl.Fragment.Length != 0)
            throw new ApiException(503, "AI_SERVICE_NOT_CONFIGURED", "AI diagnostics are not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(serviceUrl, "/internal/v1/catalog/test"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        request.Content = JsonContent.Create(new
        {
            provider.BaseUrl,
            provider.CredentialReference,
            provider.TimeoutSeconds,
            ModelKey = model?.ProviderModelKey,
            ModelType = model?.ModelType
        });

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds + 5));
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => new CatalogTestResult(true, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds),
                HttpStatusCode.UnprocessableEntity => throw new ApiException(422, "INVALID_TEST_CONFIGURATION",
                    "The provider endpoint or credential reference is not configured for testing."),
                HttpStatusCode.BadGateway => throw new ApiException(502, "CATALOG_TEST_FAILED",
                    "The provider rejected the test or returned an invalid response."),
                HttpStatusCode.ServiceUnavailable => throw new ApiException(503, "PROVIDER_UNAVAILABLE",
                    "The provider is temporarily unavailable.", retryable: true),
                HttpStatusCode.GatewayTimeout => throw TimeoutError(),
                _ => throw Unavailable()
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw TimeoutError();
        }
        catch (HttpRequestException)
        {
            throw Unavailable();
        }
    }

    private static ApiException TimeoutError() =>
        new(504, "CATALOG_TEST_TIMEOUT", "The test timed out.", retryable: true);

    private static ApiException Unavailable() =>
        new(503, "AI_SERVICE_UNAVAILABLE", "AI diagnostics are temporarily unavailable.", retryable: true);
}
