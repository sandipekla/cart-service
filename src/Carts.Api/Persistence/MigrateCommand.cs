namespace Carts.Api.Persistence;

/// <summary>
/// What the --migrate switch runs: update the database, then exit. Demo products are added
/// only in the Development environment; any other environment gets the schema and nothing else.
/// </summary>
public static partial class MigrateCommand
{
    public const string Switch = "--migrate";

    /// <returns>The process exit code: 0 when the database is up to date, 1 for any failure.</returns>
    public static async Task<int> RunAsync(Func<WebApplication> buildApp)
    {
        WebApplication? app = null;
        try
        {
            app = buildApp();
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
            await DatabaseSetup.RunAsync(db, seedDemoProducts: app.Environment.IsDevelopment(), CancellationToken.None);

            var logger = LoggerOf(app);
            LogUpToDate(logger);
            return 0;
        }
#pragma warning disable CA1031 // This is the edge of the process: every failure becomes exit code 1.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (app is null)
            {
                // The application could not even be built (bad configuration), so there is no logger yet.
                await Console.Error.WriteLineAsync($"Database setup failed: {exception.Message}");
            }
            else
            {
                var logger = LoggerOf(app);
                LogFailed(logger, exception);
            }

            return 1;
        }
        finally
        {
            if (app is not null)
            {
                await app.DisposeAsync();
            }
        }
    }

    private static ILogger LoggerOf(WebApplication app) =>
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand).FullName!);

    [LoggerMessage(Level = LogLevel.Information, Message = "The database is up to date.")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Database setup failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
