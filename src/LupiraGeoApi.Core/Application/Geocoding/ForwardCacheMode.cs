namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>How <see cref="GeocodingService.ForwardAsync"/> reads the frozen forward cache. Whatever answers is written
/// back through the normal upsert in every mode.</summary>
public enum ForwardCacheMode
{
    /// <summary>Serve frozen hits; serve a frozen empty answer until it expires.</summary>
    Default,

    /// <summary>Serve frozen hits; re-ask the geocoder for any frozen empty answer, however fresh.</summary>
    RetryEmpty,

    /// <summary>Skip the cache read entirely, overwriting even frozen hits.</summary>
    Bypass,
}
