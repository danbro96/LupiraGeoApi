using System.Text.Json;
using System.Text.Json.Serialization;

namespace LupiraGeoApi.Clients;

/// <summary>Base for clients of sibling APIs' <c>/internal</c> seams: cached Authentik client-credentials bearer in
/// prod, <c>X-Dev-User</c> + <c>X-Dev-Scopes: internal:read</c> locally. A failure returns null — the orphan sweep
/// fails closed on it, so a partial answer can never masquerade as "no references".</summary>
public abstract class InternalServiceClient(HttpClient http, InternalApiOptions opts, ILogger logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);
    private const int MaxIdsPerRequest = 1000;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public bool IsConfigured => opts.IsConfigured;

    /// <summary>POST the id batch to the seam's <c>:check</c> route, chunked to the seam's request cap; null if any
    /// chunk fails. Responses accumulate across chunks.</summary>
    protected async Task<List<TRef>?> CheckAsync<TRef>(string route, Func<PlaceRefsEnvelope<TRef>, List<TRef>> select,
        IReadOnlyList<Guid> placeIds, CancellationToken ct)
    {
        var all = new List<TRef>();
        foreach (var chunk in placeIds.Chunk(MaxIdsPerRequest))
        {
            try
            {
                var baseUrl = opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/";
                using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), route))
                {
                    Content = JsonContent.Create(new CheckRequest { PlaceIds = [.. chunk] }, options: Json),
                };
                foreach (var (key, value) in await AuthHeadersAsync(ct))
                    req.Headers.TryAddWithoutValidation(key, value);

                using var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Place-reference check {Route} returned {Status} for {Count} ids.", route, (int) resp.StatusCode, chunk.Length);
                    return null;
                }

                var body = await resp.Content.ReadFromJsonAsync<PlaceRefsEnvelope<TRef>>(Json, ct);
                if (body is null) return null;
                all.AddRange(select(body));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Place-reference check {Route} failed for {Count} ids.", route, chunk.Length);
                return null;
            }
        }

        return all;
    }

    private async Task<IReadOnlyDictionary<string, string>> AuthHeadersAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(opts.TokenUrl) && !string.IsNullOrWhiteSpace(opts.ClientId) && !string.IsNullOrWhiteSpace(opts.ClientSecret))
            return new Dictionary<string, string> { ["Authorization"] = $"Bearer {await GetTokenAsync(ct)}" };
        if (!string.IsNullOrWhiteSpace(opts.DevUser))
            return new Dictionary<string, string> { ["X-Dev-User"] = opts.DevUser!, ["X-Dev-Scopes"] = "internal:read" };
        return new Dictionary<string, string>();
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && DateTimeOffset.UtcNow < _expiresAt) return cached;
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_token is { } fresh && DateTimeOffset.UtcNow < _expiresAt) return fresh;
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = opts.ClientId!,
                ["client_secret"] = opts.ClientSecret!,
            };
            // Requesting the scope is what pulls in the audience mapping AND internal:read; binding them on the
            // provider alone is not enough — the sibling API rejects the token.
            if (!string.IsNullOrWhiteSpace(opts.Scope)) form["scope"] = opts.Scope!;
            using var resp = await http.PostAsync(opts.TokenUrl, new FormUrlEncodedContent(form), ct);
            resp.EnsureSuccessStatusCode();
            var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Client-credentials token response was empty.");
            _token = token.AccessToken ?? throw new InvalidOperationException("Token response had no access_token.");
            _expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(token.ExpiresIn ?? 300) - ExpirySkew;
            return _token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed class CheckRequest { public required List<Guid> PlaceIds { get; set; } }

    protected sealed class PlaceRefsEnvelope<TRef>
    {
        public List<TRef> Places { get; set; } = [];
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    }
}
