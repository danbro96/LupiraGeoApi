namespace LupiraGeoApi.Core.Application;

/// <summary>Outcome of a forward geocode. <c>Ok</c> carries hits; <c>Empty</c> is a definitive "no such place"
/// (safe to freeze/provision); <c>Unavailable</c> means no endpoint could be reached (transport error/timeout/429/5xx
/// after retries) — transient, NOT a no-hit, so callers must not persist a coordinate-less stub for it.</summary>
public enum GeocodeStatus { Ok, Empty, Unavailable }
