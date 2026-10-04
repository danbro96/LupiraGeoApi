using Lupira.Clients.ServiceTokens;

namespace LupiraGeoApi.Clients;

/// <summary>Connection + service-auth settings for a sibling API's <c>/internal</c> seam. Service-authed: Authentik
/// client-credentials in prod (<see cref="OutboundHopOptions.Scope"/> must request both the audience mapping and
/// <c>internal:read</c>), or <c>X-Dev-User</c>/<c>X-Dev-Scopes</c> headers locally. Unset
/// <see cref="OutboundHopOptions.BaseUrl"/> ⇒ not configured ⇒ the orphan sweep fails closed.</summary>
public abstract class InternalApiOptions : OutboundHopOptions
{
    protected InternalApiOptions() => DevScopes = "internal:read";
}
