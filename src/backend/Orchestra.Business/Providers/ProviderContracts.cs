using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestra.Api.Modules.Providers;

public sealed record ProviderDto(
    Guid ProviderId,
    string ProviderName,
    string BaseUrl,
    string? CredentialReference,
    string Status,
    int TimeoutSeconds,
    JsonElement Configuration,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateProviderRequest(
    string? ProviderName,
    string? BaseUrl,
    string? CredentialReference,
    int? TimeoutSeconds,
    JsonElement Configuration);

public sealed record UpdateProviderRequest
{
    public JsonElement ProviderName { get; init; }
    public JsonElement BaseUrl { get; init; }
    public JsonElement CredentialReference { get; init; }
    public JsonElement TimeoutSeconds { get; init; }
    public JsonElement Configuration { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}