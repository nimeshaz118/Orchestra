using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Orchestra.Api.Common;
using Orchestra.Api.Data;

namespace Orchestra.Api.Modules.Models;

public sealed class ModelsService(OrchestraDbContext db)
{
    public async Task<List<ModelDto>> ListAsync(CancellationToken ct)
    {
        var models = await db.AiModels.AsNoTracking()
            .OrderBy(m => m.DisplayName)
            .ThenBy(m => m.ProviderModelKey)
            .ToListAsync(ct);

        return models.Select(m => new ModelDto(
            m.ModelId,
            m.ProviderId,
            m.ProviderModelKey,
            m.DisplayName,
            m.ModelType,
            m.Status,
            m.ContextWindowTokens,
            m.QualityTier,
            JsonDocument.Parse(m.Capabilities).RootElement.Clone(),
            m.CreatedAt,
            m.UpdatedAt)).ToList();
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
            model = await db.AiModels.AsNoTracking()
                .Where(m => m.ModelId == modelId)
                .Select(m => new ModelDto(
                    m.ModelId, m.ProviderId, m.ProviderModelKey, m.DisplayName,
                    m.ModelType, m.Status, m.ContextWindowTokens, m.QualityTier,
                    JsonDocument.Parse(m.Capabilities).RootElement.Clone(),
                    m.CreatedAt, m.UpdatedAt))
                .SingleOrDefaultAsync(ct);
        }

        return model ?? throw new ApiException(
            404, "MODEL_NOT_FOUND", "The model was not found.");
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
        catch (PostgresException ex) when (ex.SqlState == "P0003")
        {
            await transaction.RollbackAsync(ct);
            throw new ApiException(404, "PROVIDER_NOT_FOUND", "The provider was not found.");
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
