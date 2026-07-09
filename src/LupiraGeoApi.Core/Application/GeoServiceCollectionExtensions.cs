namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the transport-neutral geo application services (geocoding, place resolver/query, saved places,
/// gazetteer import). Split out of <c>AddGeoCore</c> so the service wiring stays in one obvious place.</summary>
public static class GeoServiceCollectionExtensions
{
    public static IServiceCollection AddGeoServices(this IServiceCollection services)
    {
        services.AddScoped<LupiraGeoApi.Application.GeocodingService>();
        services.AddScoped<LupiraGeoApi.Application.PlaceResolver>();
        services.AddScoped<LupiraGeoApi.Application.PlaceQueryService>();
        services.AddScoped<LupiraGeoApi.Application.SavedPlaceService>();
        services.AddScoped<LupiraGeoApi.Application.AdminAreaService>();
        services.AddScoped<LupiraGeoApi.Application.GazetteerImporter>();
        return services;
    }
}
