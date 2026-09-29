namespace LupiraGeoApi.Core.Dtos.Curation;

/// <summary>A place the area sweep reclassifies (or, on a dry run, would reclassify) from <c>Poi</c> to <c>Area</c>.</summary>
public sealed class AreaReclassificationDto
{
    public required Guid PlaceId { get; set; }

    public required string Name { get; set; }

    public required string OsmId { get; set; }

    public required bool Applied { get; set; }
}
