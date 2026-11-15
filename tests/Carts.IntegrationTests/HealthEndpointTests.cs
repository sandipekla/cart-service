using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Carts.Api.Persistence;
using Carts.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carts.IntegrationTests;

public class HealthEndpointTests(PostgresFixture postgres)
{
    // Nothing listens on port 1, so every connection attempt is refused.
    private const string RefusingDatabase = "Host=127.0.0.1;Port=1;Database=carts;Username=carts;Password=unused";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ready_and_live_answer_200_when_the_database_answers()
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await ReadyStatusOnceWarmAsync(client));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Ready_answers_503_and_live_stays_200_when_the_database_refuses_connections()
    {
        await using var factory = new CartApiFactory(RefusingDatabase);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Ready_gives_up_on_a_database_that_accepts_the_connection_and_never_answers()
    {
        using var silentServer = new TcpListener(IPAddress.Loopback, port: 0);
        silentServer.Start();
        var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;
        await using var factory = new CartApiFactory($"Host=127.0.0.1;Port={port};Database=carts;Username=carts;Password=unused");
        using var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();

        var ready = await client.GetAsync("/health/ready", Cancellation);

        // The probe allows two seconds; without that limit the driver waits fifteen.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Readiness took {stopwatch.Elapsed}.");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Ready_answers_503_when_the_database_has_no_schema_yet()
    {
        await using var factory = new CartApiFactory(await postgres.CreateEmptyDatabaseAsync());
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Ready_answers_503_when_the_schema_is_older_than_this_build_needs()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await db.Database.MigrateAsync(Cancellation);
            db.Carts.Add(Cart.Create(TestData.NewUserId(), TestData.Now));
            await db.SaveChangesAsync(Cancellation);

            // The tables and their data are there; only the record of the newest migration is not.
            await db.Database.ExecuteSqlAsync($"DELETE FROM \"__EFMigrationsHistory\"", Cancellation);
        }

        await using var factory = new CartApiFactory(connectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready", Cancellation)).StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task The_health_endpoints_answer_get_and_nothing_that_could_change_state(string path)
    {
        await using var factory = new CartApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(path, content: null, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(path, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task A_burst_of_readiness_calls_never_opens_more_than_two_database_connections()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await db.Database.MigrateAsync(Cancellation);
        }

        await using var factory = new CartApiFactory(connectionString);
        using var client = factory.CreateClient();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => client.GetAsync("/health/ready", Cancellation)));

        // Pooled connections stay open after use, so the count now is the most that were open at once.
        var probeConnections = await CountProbeConnectionsAsync(connectionString);
        Assert.InRange(probeConnections, 1, 2);
    }

    [Fact]
    public async Task The_readiness_answer_does_not_describe_the_failure()
    {
        await using var factory = new CartApiFactory(RefusingDatabase, environment: "Production");
        using var client = factory.CreateClient();

        var body = await (await client.GetAsync("/health/ready", Cancellation)).Content.ReadAsStringAsync(Cancellation);

        Assert.Equal("Unhealthy", body);
    }

    [Fact]
    public void Outside_development_the_api_does_not_start_without_a_connection_string()
    {
        using var factory = new CartApiFactory(connectionString: null, environment: "Production");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Carts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_the_api_starts_on_its_default_connection_string()
    {
        await using var factory = new CartApiFactory(connectionString: null, environment: "Development");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Starting_the_api_never_creates_or_changes_the_schema()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new CartApiFactory(connectionString);
        using var client = factory.CreateClient();
        await client.GetAsync("/health/live", Cancellation);
        await client.GetAsync("/health/ready", Cancellation);

        Assert.Empty(await SchemaReader.TableNamesAsync(connectionString, Cancellation));
    }

    private static async Task<long> CountProbeConnectionsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = @name",
            connection);
        command.Parameters.AddWithValue("name", DatabaseHealthCheck.ApplicationName);
        return (long)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    /// <summary>
    /// The first probe opens a cold connection, which a busy machine can make slower than the
    /// probe's two seconds. Readiness may say "not yet" once or twice, half a second apart; a
    /// probe that never succeeds still fails the test.
    /// </summary>
    private static async Task<HttpStatusCode> ReadyStatusOnceWarmAsync(HttpClient client)
    {
        var status = HttpStatusCode.ServiceUnavailable;
        for (var attempt = 0; attempt < 3 && status != HttpStatusCode.OK; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
            }

            status = (await client.GetAsync("/health/ready", Cancellation)).StatusCode;
        }

        return status;
    }
}
