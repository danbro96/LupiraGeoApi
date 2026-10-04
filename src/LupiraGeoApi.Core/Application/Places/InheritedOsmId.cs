namespace LupiraGeoApi.Core.Application.Places;

/// <summary>An OSM id a past merge moved from the loser onto the survivor.</summary>
public sealed record InheritedOsmId(string Value, InheritedOsmEvidence Evidence);
