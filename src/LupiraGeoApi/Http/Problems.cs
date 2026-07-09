using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraGeoApi.Http;

/// <summary>Shared helpers for RFC 7807 ProblemDetails responses (consistent <c>application/problem+json</c>).</summary>
internal static class Problems
{
    public static ProblemHttpResult BadRequest(string detail, string? title = null) =>
        TypedResults.Problem(title: title ?? "Bad request", detail: detail, statusCode: StatusCodes.Status400BadRequest, type: "https://httpstatuses.com/400");

    public static ProblemHttpResult Forbidden(string detail, string? title = null) =>
        TypedResults.Problem(title: title ?? "Forbidden", detail: detail, statusCode: StatusCodes.Status403Forbidden, type: "https://httpstatuses.com/403");

    public static ProblemHttpResult Conflict(string detail, string? title = null) =>
        TypedResults.Problem(title: title ?? "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict, type: "https://httpstatuses.com/409");
}
