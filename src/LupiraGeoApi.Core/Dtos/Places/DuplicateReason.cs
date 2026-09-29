namespace LupiraGeoApi.Core.Dtos.Places;

public enum DuplicateReason
{
    /// <summary>Same match key — spelling variants the resolver would now collapse, left over from before it did.</summary>
    SameName,

    /// <summary>Different keys, but close enough together with similar enough names to be one real-world place.</summary>
    CoLocated,
}
