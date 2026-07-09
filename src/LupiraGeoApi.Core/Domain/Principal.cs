namespace LupiraGeoApi.Domain;

/// <summary>
/// An identity (plain document, JIT-provisioned from Authentik), local to this service. <see cref="AuthentikSub"/>
/// is the durable anchor; <see cref="Email"/> is the mutable join key. The OIDC <c>sub</c> is the only cross-service
/// join key with LupiraCalApi — each service keeps its own <see cref="Principal"/> row; the local <see cref="Id"/>
/// is never shared.
/// </summary>
public sealed class Principal
{
    public Guid Id { get; set; }
    public string AuthentikSub { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
}
