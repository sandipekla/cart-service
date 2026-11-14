using Npgsql;

namespace Carts.IntegrationTests;

/// <summary>
/// A lock held by another connection, for as long as the test keeps this object. The cart
/// lock's key is the one the API documents: a transaction-scoped advisory lock on the hash
/// of the user id.
/// </summary>
internal sealed class CartLock : IAsyncDisposable
{
    private const string Key = "hashtextextended(@userId, 0)";

    private readonly NpgsqlConnection _connection;

    private CartLock(NpgsqlConnection connection) => _connection = connection;

    /// <summary>Takes the user's cart lock.</summary>
    public static Task<CartLock> HoldAsync(string connectionString, string userId, CancellationToken cancellationToken) =>
        HoldAsync(connectionString, $"SELECT pg_advisory_xact_lock({Key})", userId, cancellationToken);

    /// <summary>Locks the row of the user's cart, as an update of it that has not committed yet does.</summary>
    public static Task<CartLock> HoldRowAsync(string connectionString, string userId, CancellationToken cancellationToken) =>
        HoldAsync(connectionString, "SELECT 1 FROM carts WHERE user_id = @userId FOR UPDATE", userId, cancellationToken);

    /// <summary>Locks the whole carts table against reading and writing, as a change of its schema does.</summary>
    public static Task<CartLock> HoldTableAsync(string connectionString, CancellationToken cancellationToken) =>
        HoldAsync(connectionString, "LOCK TABLE carts IN ACCESS EXCLUSIVE MODE", userId: null, cancellationToken);

    /// <summary>True when some other connection holds the user's cart lock at this moment.</summary>
    public static bool IsHeld(string connectionString, string userId)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = new NpgsqlCommand($"SELECT pg_try_advisory_xact_lock({Key})", connection, transaction);
        command.Parameters.AddWithValue("userId", userId);
        return !(bool)command.ExecuteScalar()!;
    }

    /// <summary>Closing the connection ends its transaction, which releases the lock.</summary>
    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static async Task<CartLock> HoldAsync(string connectionString, string sql, string? userId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        if (userId is not null)
        {
            command.Parameters.AddWithValue("userId", userId);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
        return new CartLock(connection);
    }
}
