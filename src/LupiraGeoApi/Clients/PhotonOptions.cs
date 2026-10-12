namespace LupiraGeoApi.Clients;

/// <summary>Binds <c>Photon</c> — the self-hosted address autocomplete. Unset <see cref="BaseUrl"/> ⇒ typeahead stays
/// on the regional geocoder.</summary>
public sealed class PhotonOptions
{
    public const string SectionName = "Photon";

    public string? BaseUrl { get; set; }

    /// <summary>Result language (<c>lang</c>); unset ⇒ the local name.</summary>
    public string? Language { get; set; }
}
