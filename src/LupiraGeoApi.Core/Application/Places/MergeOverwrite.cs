namespace LupiraGeoApi.Core.Application.Places;

/// <summary>A past merge that left the loser's address and/or OSM ids on the survivor. <see cref="SurvivorId"/> is the
/// live place to repair (the end of the merge chain); <see cref="OsmIds"/> are its current OSM ids.</summary>
public sealed record MergeOverwrite(
    long Seq, DateTimeOffset At, Guid LoserId, string LoserName, Guid SurvivorId, string SurvivorName,
    string? InheritedAddress, IReadOnlyList<InheritedOsmId> InheritedOsmIds, IReadOnlyList<string> OsmIds);
