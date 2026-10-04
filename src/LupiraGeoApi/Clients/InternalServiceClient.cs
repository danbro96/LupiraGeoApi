using System.Text.Json;
using Lupira.Contracts.PlaceRefs;

namespace LupiraGeoApi.Clients;

/// <summary>Base for clients of sibling APIs' <c>/internal</c> seams; service auth rides on the client's
/// <c>AddLupiraServiceToken</c> handler. A failure returns null — the orphan sweep fails closed on it, so a partial
/// answer can never masquerade as "no references".</summary>
public abstract class InternalServiceClient(HttpClient http, InternalApiOptions opts, ILogger logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxIdsPerRequest = 1000;

    public bool IsConfigured => opts.IsConfigured;

    /// <summary>POST the id batch to the seam's <c>:check</c> route, chunked to the seam's request cap; null if any
    /// chunk fails. Responses accumulate across chunks.</summary>
    protected async Task<List<PlaceReferenceCountDto>?> CheckAsync(string route, IReadOnlyList<Guid> placeIds, CancellationToken ct)
    {
        var all = new List<PlaceReferenceCountDto>();
        foreach (var chunk in placeIds.Chunk(MaxIdsPerRequest))
        {
            try
            {
                var baseUrl = opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/";
                using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), route))
                {
                    Content = JsonContent.Create(new CheckPlaceReferencesRequest { PlaceIds = [.. chunk] }, options: Json),
                };

                using var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Place-reference check {Route} returned {Status} for {Count} ids.", route, (int) resp.StatusCode, chunk.Length);
                    return null;
                }

                var body = await resp.Content.ReadFromJsonAsync<PlaceReferencesResponse>(Json, ct);
                if (body is null) return null;
                all.AddRange(body.Places);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Place-reference check {Route} failed for {Count} ids.", route, chunk.Length);
                return null;
            }
        }

        return all;
    }
}
