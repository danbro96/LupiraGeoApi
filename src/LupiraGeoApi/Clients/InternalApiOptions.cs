namespace LupiraGeoApi.Clients;

/// <summary>Connection + service-auth settings for a sibling API's <c>/internal</c> seam. Service-authed: Authentik
/// client-credentials in prod (<see cref="TokenUrl"/> + client id/secret; <see cref="Scope"/> must request both the
/// audience mapping and <c>internal:read</c>), or <c>X-Dev-User</c>/<c>X-Dev-Scopes</c> headers locally. Unset
/// <see cref="BaseUrl"/> ⇒ not configured ⇒ the orphan sweep fails closed.</summary>
public abstract class InternalApiOptions
{
    public string BaseUrl { get; set; } = "";
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }
    public string? DevUser { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>Binds <c>ContactApi</c> — the geo → LupiraContactApi hop (place-reference checks for the orphan sweep).</summary>
public sealed class ContactApiOptions : InternalApiOptions
{
    public const string SectionName = "ContactApi";
}

/// <summary>Binds <c>CalendarApi</c> — the geo → LupiraCalApi hop (place-reference checks for the orphan sweep).</summary>
public sealed class CalendarApiOptions : InternalApiOptions
{
    public const string SectionName = "CalendarApi";
}
