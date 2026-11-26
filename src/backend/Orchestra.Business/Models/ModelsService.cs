using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;
using Orchestra.Api.Modules.Providers;

namespace Orchestra.Api.Modules.Models;

public sealed class ModelsService(OrchestraDbContext db, ProvidersService providers, IMapper mapper)
{
    public async Task<List<ModelDto>> ListAsync(CancellationToken ct)
    {
        var models = await db.AiModels.AsNoTracking()
            .OrderBy(m => m.DisplayName)
            .ThenBy(m => m.ProviderModelKey)
            .ToListAsync(ct);

        return mapper.Map<List<ModelDto>>(models);
    }

    public async Task<ModelDto> GetByIdAsync(Guid modelId, CancellationToken ct)
    {
        ModelDto? model;
        if (db.Database.IsNpgsql())
        {
            var rows = await db.ReadProcedureAsync(
                "CALL ai_orchestration.api_get_model($1, 'api_rows')",
                ReadModel, ct, modelId);
            model = rows.SingleOrDefault();
        }
        else
        {
            var entity = await db.AiModels.AsNoTracking()
                .SingleOrDefaultAsync(m => m.ModelId == modelId, ct);
            model = entity is null ? null : mapper.Map<ModelDto>(entity);
        }

        return model ?? throw new ApiException(
            404, "MODEL_NOT_FOUND", "The model was not found.");
    }

    public async Task<ModelDto> UpdateAsync(
        Guid modelId, UpdateModelRequest request, Guid actorId, CancellationToken ct)
    {
        var model = await db.AiModels.SingleOrDefaultAsync(m => m.ModelId == modelId, ct)
            ?? throw new ApiException(404, "MODEL_NOT_FOUND", "The model was not found.");

        var errors = new Dictionary<string, string[]>();
        if (request.AdditionalProperties is { Count: > 0 } additionalProperties)
        {
            foreach (var field in additionalProperties.Keys)
                errors[field] = ["This field cannot be changed."];
        }

        var updateProviderModelKey = request.ProviderModelKey.ValueKind != JsonValueKind.Undefined;
        var updateDisplayName = request.DisplayName.ValueKind != JsonValueKind.Undefined;
        var updateModelType = request.ModelType.ValueKind != JsonValueKind.Undefined;
        var updateContextWindow = request.ContextWindowTokens.ValueKind != JsonValueKind.Undefined;
        var updateQualityTier = request.QualityTier.ValueKind != JsonValueKind.Undefined;
        var updateCapabilities = request.Capabilities.ValueKind != JsonValueKind.Undefined;

        if (!updateProviderModelKey && !updateDisplayName && !updateModelType &&
            !updateContextWindow && !updateQualityTier && !updateCapabilities &&
            errors.Count == 0)
            errors["request"] = ["Provide at least one model field to update."];

        var providerModelKey = ValidateText(
            request.ProviderModelKey, updateProviderModelKey,
            "providerModelKey", 200, errors);
        var displayName = ValidateText(
            request.DisplayName, updateDisplayName,
            "displayName", 150, errors);

        string? modelType = null;
        if (updateModelType)
        {
            modelType = ValidateText(request.ModelType, true, "modelType", 30, errors)
                ?.ToUpperInvariant();
            if (modelType is not null && modelType is not ("CHAT" or "EMBEDDING"))
                errors["modelType"] = ["Model type must be CHAT or EMBEDDING."];
        }

        int? contextWindowTokens = null;
        if (updateContextWindow &&
            request.ContextWindowTokens.ValueKind != JsonValueKind.Null)
        {
            if (request.ContextWindowTokens.ValueKind != JsonValueKind.Number ||
                !request.ContextWindowTokens.TryGetInt32(out var parsedContextWindow) ||
                parsedContextWindow <= 0)
                errors["contextWindowTokens"] = ["Context window tokens must be null or a positive integer."];
            else
                contextWindowTokens = parsedContextWindow;
        }

        short? qualityTier = null;
        if (updateQualityTier)
        {
            if (request.QualityTier.ValueKind != JsonValueKind.Number ||
                !request.QualityTier.TryGetInt16(out var parsedQualityTier) ||
                parsedQualityTier is < 1 or > 5)
                errors["qualityTier"] = ["Quality tier must be an integer from 1 to 5."];
            else
                qualityTier = parsedQualityTier;
        }

        if (updateCapabilities && request.Capabilities.ValueKind != JsonValueKind.Object)
            errors["capabilities"] = ["Capabilities must be a JSON object."];

        if (errors.Count > 0)
            throw new ApiException(
                422, "INVALID_MODEL_UPDATE", "One or more model fields are invalid.", errors);

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        if (updateProviderModelKey && model.ProviderModelKey != providerModelKey)
        {
            oldValues["providerModelKey"] = model.ProviderModelKey;
            newValues["providerModelKey"] = model.ProviderModelKey = providerModelKey!;
        }
        if (updateDisplayName && model.DisplayName != displayName)
        {
            oldValues["displayName"] = model.DisplayName;
            newValues["displayName"] = model.DisplayName = displayName!;
        }
        if (updateModelType && model.ModelType != modelType)
        {
            oldValues["modelType"] = model.ModelType;
            newValues["modelType"] = model.ModelType = modelType!;
        }
        if (updateContextWindow && model.ContextWindowTokens != contextWindowTokens)
        {
            oldValues["contextWindowTokens"] = model.ContextWindowTokens;
            newValues["contextWindowTokens"] = model.ContextWindowTokens = contextWindowTokens;
        }
        if (updateQualityTier && model.QualityTier != qualityTier)
        {
            oldValues["qualityTier"] = model.QualityTier;
            newValues["qualityTier"] = model.QualityTier = qualityTier!.Value;
        }
        if (updateCapabilities)
        {
            var capabilities = request.Capabilities.GetRawText();
            if (model.Capabilities != capabilities)
            {
                oldValues["capabilities"] = JsonDocument.Parse(model.Capabilities).RootElement.Clone();
                newValues["capabilities"] = request.Capabilities;
                model.Capabilities = capabilities;
            }
        }

        if (newValues.Count > 0)
        {
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedByUserId = actorId;
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogId = Guid.NewGuid(),
                ActorUserId = actorId,
                ActorRoleCode = "SUPER_ADMIN",
                Action = "MODEL_UPDATED",
                TargetType = "AI_MODEL",
                TargetId = modelId,
                OldValues = JsonSerializer.Serialize(oldValues),
                NewValues = JsonSerializer.Serialize(newValues),
                Outcome = "SUCCESS",
                CorrelationId = Guid.NewGuid(),
                OccurredAt = model.UpdatedAt
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "uq_ai_model_provider_key"
            })
            {
                throw new ApiException(
                    409, "MODEL_EXISTS",
                    "A model with this provider and provider model key already exists.");
            }
        }

        return mapper.Map<ModelDto>(model);
    }

    private static string? ValidateText(
        JsonElement value, bool provided, string field, int maxLength,
        Dictionary<string, string[]> errors)
    {
        if (!provided)
            return null;

        if (value.ValueKind != JsonValueKind.String)
        {
            errors[field] = ["Enter a string value."];
            return null;
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            errors[field] = ["This field cannot be blank."];
            return null;
        }

        if (text.Length > maxLength)
        {
            errors[field] = [$"This field cannot exceed {maxLength} characters."];
            return null;
        }

        return text;
    }

    public Task<ModelDto> EnableAsync(Guid modelId, Guid actorId, CancellationToken ct) =>
        SetStatusAsync(modelId, "ACTIVE", actorId, ct);

    public Task<ModelDto> DisableAsync(Guid modelId, Guid actorId, CancellationToken ct) =>
        SetStatusAsync(modelId, "INACTIVE", actorId, ct);

    private async Task<ModelDto> SetStatusAsync(
        Guid modelId, string status, Guid actorId, CancellationToken ct)
    {
        var model = await db.AiModels.SingleOrDefaultAsync(m => m.ModelId == modelId, ct)
            ?? throw new ApiException(404, "MODEL_NOT_FOUND", "The model was not found.");

        if (model.Status != status)
        {
            var previousStatus = model.Status;
            model.Status = status;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedByUserId = actorId;
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogId = Guid.NewGuid(),
                ActorUserId = actorId,
                ActorRoleCode = "SUPER_ADMIN",
                Action = status == "ACTIVE" ? "MODEL_ENABLED" : "MODEL_DISABLED",
                TargetType = "AI_MODEL",
                TargetId = modelId,
                OldValues = JsonSerializer.Serialize(new { status = previousStatus }),
                NewValues = JsonSerializer.Serialize(new { status }),
                Outcome = "SUCCESS",
                CorrelationId = Guid.NewGuid(),
                OccurredAt = model.UpdatedAt
            });
            await db.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(modelId, ct);
    }

    public async Task<ModelDto> CreateAsync(
        CreateModelRequest request, Guid actorId, CancellationToken ct)
    {
        var providerModelKey = request.ProviderModelKey?.Trim();
        var displayName = request.DisplayName?.Trim();
        var modelType = request.ModelType?.Trim().ToUpperInvariant();

        var errors = new Dictionary<string, string[]>();
        if (request.ProviderId == Guid.Empty)
            errors["providerId"] = ["Provider ID is required."];
        if (string.IsNullOrWhiteSpace(providerModelKey))
            errors["providerModelKey"] = ["Provider model key is required."];
        else if (providerModelKey.Length > 200)
            errors["providerModelKey"] = ["Provider model key cannot exceed 200 characters."];
        if (string.IsNullOrWhiteSpace(displayName))
            errors["displayName"] = ["Display name is required."];
        else if (displayName.Length > 150)
            errors["displayName"] = ["Display name cannot exceed 150 characters."];
        if (modelType is not ("CHAT" or "EMBEDDING"))
            errors["modelType"] = ["Model type must be CHAT or EMBEDDING."];
        if (request.ContextWindowTokens is <= 0)
            errors["contextWindowTokens"] = ["Context window tokens must be greater than zero."];
        if (request.QualityTier is < 1 or > 5)
            errors["qualityTier"] = ["Quality tier must be between 1 and 5."];
        if (request.Capabilities.ValueKind != JsonValueKind.Object)
            errors["capabilities"] = ["Capabilities must be a JSON object."];
        if (errors.Count > 0)
            throw new ApiException(400, "INVALID_MODEL", "The model request is invalid.", errors);

        if (!db.Database.IsNpgsql())
            throw new InvalidOperationException("Model creation requires PostgreSQL.");

        await providers.GetAsync(request.ProviderId, ct);

        var modelId = await CallCreateProcedureAsync(
            request.ProviderId, providerModelKey!, displayName!, modelType!,
            request.ContextWindowTokens, request.QualityTier,
            request.Capabilities.GetRawText(), actorId, ct);

        var rows = await db.ReadProcedureAsync(
            "CALL ai_orchestration.api_get_model($1, 'api_rows')",
            ReadModel, ct, modelId);
        return rows.SingleOrDefault() ?? throw new ApiException(
            500, "MODEL_CREATE_FAILED", "The model was created but could not be read back.");
    }

    private async Task<Guid> CallCreateProcedureAsync(
        Guid providerId, string providerModelKey, string displayName, string modelType,
        int? contextWindowTokens, short qualityTier, string capabilities,
        Guid actorId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "CALL ai_orchestration.api_create_model($1, $2, $3, $4, $5, $6, $7, $8, $9)", connection)
        {
            Transaction = (NpgsqlTransaction)transaction.GetDbTransaction()
        };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = providerId });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = providerModelKey });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = displayName });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = modelType });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Integer,
            Value = contextWindowTokens.HasValue ? contextWindowTokens.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Smallint, Value = qualityTier });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = capabilities });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = actorId });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = DBNull.Value });

        try
        {
            Guid modelId;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new ApiException(500, "MODEL_CREATE_FAILED", "The model could not be created.");
                modelId = reader.GetGuid(0);
            }

            await transaction.CommitAsync(ct);
            return modelId;
        }
        catch (PostgresException ex) when (ex.SqlState is "P0002" or PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct);
            throw new ApiException(409, "MODEL_EXISTS",
                "A model with this provider and provider model key already exists.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            await transaction.RollbackAsync(ct);
            throw new ApiException(404, "PROVIDER_NOT_FOUND", "The provider was not found.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static ModelDto ReadModel(NpgsqlDataReader row) => new(
        row.GetGuid(0), row.GetGuid(1), row.GetString(2), row.GetString(3),
        row.GetString(4), row.GetString(5),
        row.IsDBNull(6) ? null : row.GetInt32(6), row.GetInt16(7),
        JsonDocument.Parse(row.GetString(8)).RootElement.Clone(),
        row.GetDateTime(9), row.GetDateTime(10));
}
