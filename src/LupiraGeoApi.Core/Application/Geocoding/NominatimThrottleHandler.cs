namespace LupiraGeoApi.Core.Application.Geocoding;

/// <summary>Applies the <see cref="NominatimRateGate"/> to every request on the fallback HttpClient pipeline.</summary>
public sealed class NominatimThrottleHandler(NominatimRateGate gate) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await gate.WaitTurnAsync(cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
