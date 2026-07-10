using LupiraGeoApi.Application;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the transport-neutral geo application services (geocoding, place resolver/query/merge, saved
/// places, gazetteer import) and the Nominatim HTTP clients — a plain 5 s-timeout primary and a throttled fallback
/// (public-endpoint policy: ≤1 req/s via <see cref="NominatimRateGate"/>). Split out of <c>AddGeoCore</c> so the
/// service wiring stays in one obvious place.</summary>
public static class GeoServiceCollectionExtensions
{
    public static IServiceCollection AddGeoServices(this IServiceCollection services)
    {
        services.AddOptions<NominatimOptions>().BindConfiguration("Nominatim");
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<NominatimRateGate>();
        services.AddTransient<NominatimThrottleHandler>();
        services.AddHttpClient(GeocodingService.PrimaryClientName, c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddHttpClient(GeocodingService.FallbackClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<NominatimThrottleHandler>();

        services.AddScoped<GeocodingService>();
        services.AddScoped<PlaceResolver>();
        services.AddScoped<PlaceQueryService>();
        services.AddScoped<PlaceMergeService>();
        services.AddScoped<SavedPlaceService>();
        services.AddScoped<AdminAreaService>();
        services.AddScoped<GazetteerImporter>();
        return services;
    }
}
