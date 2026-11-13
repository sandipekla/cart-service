using Npgsql;

namespace Carts.IntegrationTests;

/// <summary>Reads what is actually in a database, without going through the application's model.</summary>
internal static class SchemaReader
{
    public static Task<List<string>> TableNamesAsync(string connectionString, CancellationToken cancellationToken) =>
        QueryAsync(
            connectionString,
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name",
            cancellationToken);

    public static Task<List<string>> ColumnsAsync(string connectionString, CancellationToken cancellationToken) =>
        QueryAsync(
            connectionString,
            """
            SELECT table_name || '.' || column_name || ' ' || format_type(a.atttypid, a.atttypmod)
                   || CASE WHEN a.attnotnull THEN ' not null' ELSE ' null' END
            FROM information_schema.columns c
            JOIN pg_attribute a ON a.attrelid = ('public.' || quote_ident(c.table_name))::regclass AND a.attname = c.column_name
            WHERE c.table_schema = 'public' AND c.table_name <> '__EFMigrationsHistory'
            ORDER BY c.table_name, c.ordinal_position
            """,
            cancellationToken);

    public static Task<List<string>> ConstraintsAndIndexesAsync(string connectionString, CancellationToken cancellationToken) =>
        QueryAsync(
            connectionString,
            """
            SELECT conname || ': ' || pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE connamespace = 'public'::regnamespace AND conrelid <> 'public."__EFMigrationsHistory"'::regclass
            UNION ALL
            SELECT indexname || ': ' || regexp_replace(indexdef, '^.* USING ', '')
            FROM pg_indexes
            WHERE schemaname = 'public' AND indexname LIKE 'ux\_%'
            ORDER BY 1
            """,
            cancellationToken);

    private static async Task<List<string>> QueryAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
