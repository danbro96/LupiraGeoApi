namespace LupiraGeoApi.Domain;

/// <summary>
/// A per-principal saved place / personal label (Marten document, <c>geo_user</c> schema): "Home", "Work", a star,
/// a personal name over a shared gazetteer <see cref="Place"/>. Private and owner-scoped — this is where personal
/// labels live so two people's "Home" never collide in the shared catalog. Points at a gazetteer
/// <see cref="PlaceId"/> when matched; otherwise carries a raw coordinate.
/// </summary>
public sealed class SavedPlace
{
    public Guid Id { get; set; }
    public Guid PrincipalId { get; set; }

    public Guid? PlaceId { get; set; }
    public double? RawLat { get; set; }
    public double? RawLon { get; set; }

    public string Label { get; set; } = "";
    public string? Icon { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
