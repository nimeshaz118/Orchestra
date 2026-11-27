using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Orchestra.Api.Common;
using Orchestra.Api.Modules.Models;
using Orchestra.Api.Modules.Providers;

namespace Orchestra.Api.Modules.Catalog;

public sealed class AiDiagnosticsClient(HttpClient client, IConfiguration configuration)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["AiService:BaseUrl"]) ||
        !string.IsNullOrWhiteSpace(configuration["AiService:ServiceToken"]);

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, "/internal/v1/health/ready");
        if (request is null)
            return false;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<CatalogTestResult> TestAsync(ProviderDto provider, ModelDto? model, CancellationToken ct) =>
        (await ProbeAsync(provider, model, ct)).ToResponse();

    internal async Task<CatalogProbeResult> ProbeAsync(
        ProviderDto provider, ModelDto? model, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, "/internal/v1/catalog/test");
        if (request is null)
            return Failure(new ApiException(503, "AI_SERVICE_NOT_CONFIGURED", "AI diagnostics are not configured."));
        request.Content = JsonContent.Create(new
        {
            provider.BaseUrl,
            provider.CredentialReference,
            provider.TimeoutSeconds,
            ModelKey = model?.ProviderModelKey,
            ModelType = model?.ModelType,
            Adapter = model is null ? null : ReadOption(provider.Configuration, "adapter"),
            TestText = model is null ? null : ReadOption(provider.Configuration, "testText"),
            TestMaxOutputTokens = model is null ? null : ReadOption(provider.Configuration, "testMaxOutputTokens")
        });

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds + 5));
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => new CatalogProbeResult((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds),
                HttpStatusCode.UnprocessableEntity => Failure(new ApiException(422, "INVALID_TEST_CONFIGURATION",
                    "The provider endpoint or credential reference is not configured for testing.")),
                HttpStatusCode.BadGateway => Failure(new ApiException(502, "CATALOG_TEST_FAILED",
                    "The provider rejected the test or returned an invalid response."), providerFailure: true),
                HttpStatusCode.ServiceUnavailable => Failure(new ApiException(503, "PROVIDER_UNAVAILABLE",
                    "The provider is temporarily unavailable.", retryable: true), providerFailure: true),
                HttpStatusCode.GatewayTimeout => Failure(TimeoutError(), providerFailure: true),
                _ => Failure(Unavailable())
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(TimeoutError(), errorCategory: "AI_SERVICE_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return Failure(Unavailable());
        }
    }

    private static JsonElement? ReadOption(JsonElement configuration, string name) =>
        configuration.ValueKind == JsonValueKind.Object && configuration.TryGetProperty(name, out var value)
            ? value
            : null;

    private HttpRequestMessage? CreateRequest(HttpMethod method, string path)
    {
        var serviceToken = configuration["AiService:ServiceToken"];
        if (string.IsNullOrWhiteSpace(serviceToken) ||
            !Uri.TryCreate(configuration["AiService:BaseUrl"], UriKind.Absolute, out var serviceUrl) ||
            serviceUrl.Scheme is not ("http" or "https") ||
            serviceUrl.UserInfo.Length != 0 || serviceUrl.Query.Length != 0 || serviceUrl.Fragment.Length != 0)
            return null;

        return new HttpRequestMessage(method, new Uri(serviceUrl, path))
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", serviceToken) }
        };
    }

    private static ApiException TimeoutError() =>
        new(504, "CATALOG_TEST_TIMEOUT", "The test timed out.", retryable: true);

    private static ApiException Unavailable() =>
        new(503, "AI_SERVICE_UNAVAILABLE", "AI diagnostics are temporarily unavailable.", retryable: true);

    private static CatalogProbeResult Failure(ApiException error, bool providerFailure = false, string? errorCategory = null) =>
        new(0, error, providerFailure, errorCategory ?? error.Code);
}

internal sealed record CatalogProbeResult(
    long LatencyMs, ApiException? Error = null, bool ProviderFailure = false, string? SafeErrorCategory = null)
{
    internal bool? ProviderSucceeded => Error is null ? true : ProviderFailure ? false : null;

    internal CatalogTestResult ToResponse() => Error is null ? new CatalogTestResult(true, LatencyMs) : throw Error;
}
