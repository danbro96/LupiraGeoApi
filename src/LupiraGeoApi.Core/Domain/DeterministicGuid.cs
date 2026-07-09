using System.Security.Cryptography;
using System.Text;

namespace LupiraGeoApi.Domain;

/// <summary>Stable Guid derived from a natural key — so a re-run (e.g. a daily rollup or a re-grant) lands on the
/// same id and upserts rather than duplicating.</summary>
public static class DeterministicGuid
{
    public static Guid From(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
