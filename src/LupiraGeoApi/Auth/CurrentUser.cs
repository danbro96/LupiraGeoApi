using LupiraGeoApi.Application;
using LupiraGeoApi.Domain;
using System.Security.Claims;

namespace LupiraGeoApi.Auth;

/// <summary>Reads the calling principal's OIDC claims (or the dev header) and resolves them — JIT-provisioning on first
/// login — to the local <see cref="Principal"/> via the Core <see cref="PrincipalDirectory"/>.</summary>
public sealed class CurrentUser(IHttpContextAccessor http, PrincipalDirectory directory)
{
    public async Task<Principal> GetAsync(CancellationToken ct = default)
    {
        var principal = http.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context available.");

        var sub = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email) ?? "";
        var name = principal.FindFirstValue("name") ?? principal.Identity?.Name;
        if (sub is null && string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Authenticated principal has no subject or email claim.");

        return await directory.ResolveOrProvisionAsync(sub, email, name, ct);
    }
}
