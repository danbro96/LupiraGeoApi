using LupiraGeoApi.Core.Domain;

namespace LupiraGeoApi.Core.Application.Places;

/// <summary>The result of resolving free text: the resulting <see cref="Place"/> (null only when the geocoder was
/// unreachable) and how it landed. See <see cref="PlaceResolution"/>.</summary>
public readonly record struct ResolveOutcome(Place? Place, PlaceResolution Resolution);
