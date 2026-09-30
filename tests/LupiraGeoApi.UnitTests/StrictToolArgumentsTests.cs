using System.Text.Json;
using System.Text.Json.Nodes;
using LupiraGeoApi.Mcp;
using ModelContextProtocol.Server;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class StrictToolArgumentsTests
{
    private static string? Check(Delegate tool, string argumentsJson)
    {
        var t = McpServerTool.Create(tool, new() { Name = "t" }).ProtocolTool;
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        return StrictToolArguments.Check(t.Name, t.InputSchema, args);
    }

    private static readonly Func<Guid, string> GetById = id => "x";
    private static readonly Func<string?, int?, string> Search = SearchTool;
    private static readonly Func<BatchRequest, string> Batch = request => "x";

    [Fact]
    public void Declared_arguments_pass() =>
        Assert.Null(Check(GetById, """{"id":"cc0aa957-52a8-463c-9e88-eff333f01921"}"""));

    [Fact]
    public void Misnamed_required_argument_names_both_sides() =>
        Assert.Equal("Invalid arguments for 't': unknown placeId; missing required id. Accepts: id (required).",
            Check(GetById, """{"placeId":"cc0aa957-52a8-463c-9e88-eff333f01921"}"""));

    [Fact]
    public void Unknown_optional_argument_is_rejected() =>
        Assert.Equal("Invalid arguments for 't': unknown query. Accepts: q, limit.", Check(Search, """{"query":"Torsby"}"""));

    [Fact]
    public void Top_level_names_are_ordinal() =>
        Assert.StartsWith("Invalid arguments for 't': unknown Id; missing required id.", Check(GetById, """{"Id":"x"}"""));

    [Fact]
    public void No_arguments_reports_missing_required() =>
        Assert.StartsWith("Invalid arguments for 't': missing required id.", StrictToolArguments.Check("t",
            McpServerTool.Create(GetById, new() { Name = "t" }).ProtocolTool.InputSchema, null));

    [Fact]
    public void Nested_unknown_field_is_reported_with_its_path_and_level() =>
        Assert.Equal(
            "Invalid arguments for 't': unknown request.items[1].parentKey. "
            + "At request.items[]: accepts title (required), outbound, parent, metadata, tags.",
            Check(Batch, """{"request":{"items":[{"title":"a"},{"title":"b","parentKey":"k"}]}}"""));

    [Fact]
    public void Nested_missing_required_field_is_reported() =>
        Assert.StartsWith("Invalid arguments for 't': missing required request.items[0].title.",
            Check(Batch, """{"request":{"items":[{}]}}"""));

    [Fact]
    public void Nested_names_are_case_insensitive() =>
        Assert.Null(Check(Batch, """{"request":{"Items":[{"Title":"a","OUTBOUND":{"from":"x"}}]}}"""));

    [Fact]
    public void Refs_are_followed() =>
        Assert.Equal(
            "Invalid arguments for 't': unknown request.items[0].parent.outbound.bad. At request.items[].parent.outbound: accepts from (required), note.",
            Check(Batch, """{"request":{"items":[{"title":"a","parent":{"title":"p","outbound":{"from":"x","bad":1}}}]}}"""));

    [Fact]
    public void Free_form_objects_are_not_checked() =>
        Assert.Null(Check(Batch, """{"request":{"items":[{"title":"a","metadata":{"any":1},"tags":{"k":"v"}}]}}"""));

    [Fact]
    public void Each_level_is_listed_once() =>
        Assert.Equal(
            "Invalid arguments for 't': unknown request.items[0].x; unknown request.items[1].y. "
            + "At request.items[]: accepts title (required), outbound, parent, metadata, tags.",
            Check(Batch, """{"request":{"items":[{"title":"a","x":1},{"title":"b","y":2}]}}"""));

    private static string SearchTool(string? q = null, int? limit = null) => "x";

    public sealed class Leg
    {
        public required string From { get; set; }

        public string? Note { get; set; }
    }

    public sealed class Item
    {
        public required string Title { get; set; }

        public Leg? Outbound { get; set; }

        public Item? Parent { get; set; }

        public JsonNode? Metadata { get; set; }

        public Dictionary<string, string>? Tags { get; set; }
    }

    public sealed class BatchRequest
    {
        public required List<Item> Items { get; set; }
    }
}
