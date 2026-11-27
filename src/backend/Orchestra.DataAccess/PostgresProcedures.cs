using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Orchestra.Api.Data;

public static class PostgresProcedures
{
    public static async Task<List<T>> ReadProcedureAsync<T>(
        this OrchestraDbContext db, string call, Func<NpgsqlDataReader, T> read,
        CancellationToken ct, params object[] arguments)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // PostgreSQL cursors live in a transaction. Npgsql sends the batch in one
            // round trip and supplies an implicit transaction if none is already open.
            await using var batch = new NpgsqlBatch(connection);
            var procedure = new NpgsqlBatchCommand(call);
            Bind(procedure.Parameters, arguments);
            batch.BatchCommands.Add(procedure);
            batch.BatchCommands.Add(new NpgsqlBatchCommand("FETCH ALL FROM api_rows"));
            batch.BatchCommands.Add(new NpgsqlBatchCommand("CLOSE api_rows"));

            await using var reader = await batch.ExecuteReaderAsync(ct);

            var rows = new List<T>();
            while (await reader.ReadAsync(ct))
                rows.Add(read(reader));
            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public static async Task<bool> CallBooleanProcedureAsync(
        this OrchestraDbContext db, string call, CancellationToken ct, params object[] arguments)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(call, connection);
            Bind(command.Parameters, arguments);
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void Bind(NpgsqlParameterCollection parameters, object[] arguments)
    {
        foreach (var argument in arguments)
            parameters.Add(new NpgsqlParameter { Value = argument });
    }
}
