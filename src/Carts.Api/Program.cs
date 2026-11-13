using Carts.Api.Health;
using Carts.Api.Persistence;

// The switch is taken out before the host reads the arguments. Left in, the host would read
// it as a setting and swallow the argument that follows it.
var hostArgs = args.Where(argument => argument != MigrateCommand.Switch).ToArray();

// Schema changes are a step of their own. The API never migrates while starting or serving.
if (hostArgs.Length != args.Length)
{
    return await MigrateCommand.RunAsync(() => BuildApp(hostArgs));
}

await using var app = BuildApp(hostArgs);

app.MapHealthEndpoints();

await app.RunAsync();
return 0;

static WebApplication BuildApp(string[] hostArgs)
{
    var builder = WebApplication.CreateBuilder(hostArgs);

    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
    builder.Services.AddCartPersistence(builder.Configuration);

    return builder.Build();
}

/// <summary>Lets the integration tests start the real application.</summary>
public partial class Program;
