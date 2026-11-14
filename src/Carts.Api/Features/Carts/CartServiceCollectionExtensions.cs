namespace Carts.Api.Features.Carts;

public static class CartServiceCollectionExtensions
{
    public static IServiceCollection AddCartFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CartOptions>()
            .Bind(configuration.GetSection(CartOptions.SectionName))
            .Validate(
                cart => cart.LockTimeoutMs is >= CartOptions.MinLockTimeoutMs and <= CartOptions.MaxLockTimeoutMs,
                $"Cart:LockTimeoutMs must be from {CartOptions.MinLockTimeoutMs} to {CartOptions.MaxLockTimeoutMs} milliseconds.")
            .ValidateOnStart();

        services.AddScoped<CartService>();

        return services;
    }
}
