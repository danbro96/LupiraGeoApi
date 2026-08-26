namespace LupiraGeoApi.Core.Application;

/// <summary>
/// The transport-neutral outcome of a service operation. Each surface's adapter maps it to its own wire
/// shape (REST → <c>TypedResults</c> via <c>OpResultMap</c>). Expected outcomes are values, not exceptions.
/// </summary>
public enum OpStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
    Conflict,
}
