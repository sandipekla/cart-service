using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Carts.IntegrationTests;

/// <summary>Runs the real executable the way a deployment would: as its own process, with --migrate.</summary>
public class MigrateCommandTests(PostgresFixture postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Outside_development_it_creates_the_schema_adds_no_data_and_exits()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();

        var run = await RunApiAsync("Production", connectionString, "--migrate");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Equal(
            ["__EFMigrationsHistory", "cart_items", "carts", "products"],
            await SchemaReader.TableNamesAsync(connectionString, Cancellation));
        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(0, await db.Products.CountAsync(Cancellation));
    }

    [Fact]
    public async Task In_development_it_also_adds_the_demo_catalog()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();

        var run = await RunApiAsync("Development", connectionString, "--migrate");

        Assert.Equal(0, run.ExitCode);
        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(8, await db.Products.CountAsync(Cancellation));
    }

    [Fact]
    public async Task A_failure_exits_with_a_non_zero_code_and_keeps_the_password_out_of_the_output()
    {
        const string unreachable = "Host=127.0.0.1;Port=1;Database=carts;Username=carts;Password=a-secret-nobody-should-see";

        var run = await RunApiAsync("Production", unreachable, "--migrate");

        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("a-secret-nobody-should-see", run.Output, StringComparison.Ordinal);
        Assert.Contains("Database setup failed", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_connection_string_also_exits_with_code_1()
    {
        var run = await RunApiAsync("Production", "Host=127.0.0.1;Port=not-a-number;Database=carts;Username=carts;Password=a-secret-nobody-should-see", "--migrate");

        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("a-secret-nobody-should-see", run.Output, StringComparison.Ordinal);
        Assert.Contains("Database setup failed", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_setting_given_after_the_switch_is_still_honoured()
    {
        const string wrongDatabase = "Host=127.0.0.1;Port=1;Database=carts;Username=carts;Password=unused";
        var rightDatabase = await postgres.CreateEmptyDatabaseAsync();

        var run = await RunApiAsync("Production", wrongDatabase, "--migrate", $"--ConnectionStrings:Carts={rightDatabase}");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("carts", await SchemaReader.TableNamesAsync(rightDatabase, Cancellation));
    }

    private static async Task<(int ExitCode, string Output)> RunApiAsync(string environment, string? connectionString, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Carts.Api.dll"));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = environment;
        if (connectionString is null)
        {
            startInfo.Environment.Remove("ConnectionStrings__Carts");
        }
        else
        {
            startInfo.Environment["ConnectionStrings__Carts"] = connectionString;
        }

        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync(Cancellation);
        var error = process.StandardError.ReadToEndAsync(Cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // It did not exit, which means it started serving instead of running the command.
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The API did not exit after --migrate.");
        }

        return (process.ExitCode, await output + await error);
    }
}
