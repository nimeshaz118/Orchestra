using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestra.Api.Modules.Models;

public sealed record CreateModelRequest(
    Guid ProviderId,
    string ProviderModelKey,
    string DisplayName,
    string ModelType,
    int? ContextWindowTokens,
    short QualityTier,
    JsonElement Capabilities);

public sealed record UpdateModelRequest
{
    public JsonElement ProviderModelKey { get; init; }
    public JsonElement DisplayName { get; init; }
    public JsonElement ModelType { get; init; }
    public JsonElement ContextWindowTokens { get; init; }
    public JsonElement QualityTier { get; init; }
    public JsonElement Capabilities { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record ModelDto(
    Guid Id,
    Guid ProviderId,
    string ProviderModelKey,
    string DisplayName,
    string ModelType,
    string Status,
    int? ContextWindowTokens,
    short QualityTier,
    JsonElement Capabilities,
    DateTime CreatedAt,
    DateTime UpdatedAt);
