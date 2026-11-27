using System.Text.Json;

namespace Orchestra.Api.Modules.Models;

public sealed record CreateModelRequest(
    Guid ProviderId,
    string ProviderModelKey,
    string DisplayName,
    string ModelType,
    int? ContextWindowTokens,
    short QualityTier,
    JsonElement Capabilities);

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
