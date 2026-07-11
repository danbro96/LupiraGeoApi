using LupiraGeoApi.Data;
using LupiraGeoApi.Domain;

namespace LupiraGeoApi.Application;

/// <summary>Stages a <see cref="CurationEvent"/> onto the EF change tracker so it commits in the <b>same</b>
/// <c>SaveChangesAsync</c> as the curation change it describes — the audit can never drift from the data. The
/// <c>UtcNow</c> stamp here is a command-time input, not a replay concern (see <c>docs/event-sourcing.md</c>).</summary>
public static class CurationLog
{
    public static void Record(this GeoDbContext db, Guid placeId, CurationAction action, Guid? actor,
        Guid? relatedPlaceId = null, string? detail = null) =>
        db.CurationLog.Add(new CurationEvent
        {
            PlaceId = placeId,
            Action = action,
            ActorPrincipalId = actor,
            At = DateTimeOffset.UtcNow,
            RelatedPlaceId = relatedPlaceId,
            Detail = detail,
        });
}
