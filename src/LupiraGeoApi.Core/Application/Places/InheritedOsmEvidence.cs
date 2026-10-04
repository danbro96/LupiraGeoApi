namespace LupiraGeoApi.Core.Application.Places;

/// <summary>Why <see cref="MergeOverwriteReport"/> attributes a survivor's OSM id to the merged-away loser.</summary>
public enum InheritedOsmEvidence
{
    /// <summary>The loser's curation log added the id by hand before the merge.</summary>
    LoserLog,

    /// <summary>A frozen geocode hit for the id sits exactly at the loser's coordinates or address.</summary>
    LoserGeocodeFix,
}
