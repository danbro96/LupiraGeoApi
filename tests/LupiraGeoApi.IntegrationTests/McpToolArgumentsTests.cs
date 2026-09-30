using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraGeoApi.IntegrationTests;

/// <summary>Over the real MCP transport, a call with names the tool schema doesn't declare is refused with a readable
/// error instead of an opaque one or a silently dropped argument.</summary>
public sealed class McpToolArgumentsTests(GeoApiTestFactory factory) : IntegrationTest(factory)
{
    private async Task<McpClient> ConnectAsync()
    {
        var http = Factory.ApiClient("alice@x.test");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string ErrorText(CallToolResult result)
    {
        Assert.True(result.IsError);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    [Fact]
    public async Task Misnamed_required_argument_names_both_sides()
    {
        await using var mcp = await ConnectAsync();
        var result = await mcp.CallToolAsync("get_place", new Dictionary<string, object?> { ["placeId"] = Guid.NewGuid() });

        Assert.Equal("Invalid arguments for 'get_place': unknown placeId; missing required id. Accepts: id (required).",
            ErrorText(result));
    }

    [Fact]
    public async Task Unknown_optional_argument_is_rejected_not_ignored()
    {
        await using var mcp = await ConnectAsync();
        var result = await mcp.CallToolAsync("search_places", new Dictionary<string, object?> { ["query"] = "Torsby" });

        Assert.StartsWith("Invalid arguments for 'search_places': unknown query. Accepts: q, ", ErrorText(result));
    }

    [Fact]
    public async Task Declared_arguments_reach_the_tool()
    {
        await using var mcp = await ConnectAsync();
        var result = await mcp.CallToolAsync("search_places", new Dictionary<string, object?> { ["q"] = "Torsby" });

        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public async Task Every_tool_rejects_an_undeclared_argument()
    {
        await using var mcp = await ConnectAsync();
        var tools = await mcp.ListToolsAsync();
        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            var result = await mcp.CallToolAsync(tool.Name, new Dictionary<string, object?> { ["__undeclared"] = 1 });

            Assert.StartsWith($"Invalid arguments for '{tool.Name}': unknown __undeclared", ErrorText(result));
        }
    }
}
