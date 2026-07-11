namespace LupiraGeoApi.Domain;

/// <summary>
/// An append-only record of a human/system curation decision on a shared gazetteer <see cref="Place"/> (EF, <c>geo</c>
/// schema). This is the one thing that is <b>unbackfillable</b>: who verified/renamed/aliased/merged a place, and when.
/// Written in the same transaction as the change it describes, so the trail can never drift from the data. It is also
/// the proto-event-stream — if place curation later becomes event-sourced (see <c>docs/event-sourcing.md</c>), these
/// rows are the replay seed, which is why <see cref="Action"/> uses stable names and no derived values are stored.
/// </summary>
/// <remarks>Not written for <see cref="SavedPlace"/>: those are private per-principal PII where the owner is the actor
/// and an immutable surviving trail would fight GDPR hard-delete — provenance there is the row's own timestamps.</remarks>
public sealed class CurationEvent
{
    /// <summary>Monotonic sequence (bigserial) — insertion order is the log order and the future stream position.</summary>
    public long Seq { get; set; }

    /// <summary>The place the decision was about. No FK: the log outlives the entity's current shape (an event-store mindset).</summary>
    public Guid PlaceId { get; set; }

    public CurationAction Action { get; set; }

    /// <summary>Who decided. Null = system/import (no authenticated actor).</summary>
    public Guid? ActorPrincipalId { get; set; }

    /// <summary>Server clock at the time of the decision.</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>The other place a decision references — the survivor for <see cref="CurationAction.Merged"/>.</summary>
    public Guid? RelatedPlaceId { get; set; }

    /// <summary>Action payload: the new name/category, the alias text. Free-form and human-readable, never parsed for logic.</summary>
    public string? Detail { get; set; }
}
