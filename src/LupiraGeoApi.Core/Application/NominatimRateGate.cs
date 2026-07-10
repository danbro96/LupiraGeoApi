namespace LupiraGeoApi.Application;

/// <summary>Serializes outbound calls to the public Nominatim endpoint: callers queue and each is released no less
/// than <see cref="MinInterval"/> after the previous one (public usage policy caps at 1 req/s). Singleton, so the
/// spacing holds across concurrent scopes.</summary>
public sealed class NominatimRateGate(TimeProvider time)
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1100);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public async Task WaitTurnAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _nextAllowed - time.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, time, ct);
            _nextAllowed = time.GetUtcNow() + MinInterval;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Applies the <see cref="NominatimRateGate"/> to every request on the fallback HttpClient pipeline.</summary>
public sealed class NominatimThrottleHandler(NominatimRateGate gate) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await gate.WaitTurnAsync(cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
