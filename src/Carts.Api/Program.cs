using System.Text.Json.Serialization;
using Carts.Api.ApiDocs;
using Carts.Api.Auth;
using Carts.Api.Features.Carts;
using Carts.Api.Health;
using Carts.Api.Json;
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

// Every error leaves in one format (RFC 9457 problem details), including those raised by
// the framework itself: an unhandled exception, an unknown route, a missing token.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseNoStoreForCartAnswers();
app.UseCartAuth();

app.MapHealthEndpoints();
app.MapCartEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDevTokenEndpoint();
    app.MapCartApiDocs();
    app.WarnAboutDevelopmentAuthentication();
}

await app.RunAsync();
return 0;

static WebApplication BuildApp(string[] hostArgs)
{
    var builder = WebApplication.CreateBuilder(hostArgs);

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.AddServerHeader = false;

        // The largest body this API expects is a few dozen bytes.
        options.Limits.MaxRequestBodySize = 16 * 1024;
    });

    // A body that cannot be read is the caller's mistake (400), in every environment. By
    // default the framework throws instead in Development, which would surface as a 500.
    builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<FailedRequestLogger>();
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        // A number is a number: "2" in quotes is not accepted where a quantity is expected.
        options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;

        // A body that names the same field twice is refused, not resolved by "the last one wins".
        options.SerializerOptions.AllowDuplicateProperties = false;
        options.SerializerOptions.Converters.Add(new PlainDecimalJsonConverter());
    });

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddCartPersistence(builder.Configuration);
    builder.Services.AddCartAuth(builder.Configuration, builder.Environment);
    builder.Services.AddCartFeature(builder.Configuration);

    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddCartApiDocs();
    }

    return builder.Build();
}

/// <summary>Lets the integration tests start the real application.</summary>
public partial class Program;
