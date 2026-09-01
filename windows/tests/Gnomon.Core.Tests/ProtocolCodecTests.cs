using System.Text.Json.Nodes;
using Gnomon.Core;

namespace Gnomon.Core.Tests;

public class ProtocolCodecTests
{
    [Fact]
    public void UsageFixtureUsesDeltaContract()
    {
        var json = JsonNode.Parse(ProtocolCodec.ReportUsage(1, new("alex", "pc", "games", 3, "game.exe")))!;
        Assert.Equal("call_service", json["type"]!.GetValue<string>());
        Assert.Equal("report_usage", json["service"]!.GetValue<string>());
        Assert.Equal(3, json["service_data"]!["minutes"]!.GetValue<int>());
        Assert.Null(json["service_data"]!["app_id"]);
        Assert.Null(json["service_data"]!["kind"]);
        Assert.Equal(4, json["service_data"]!.AsObject().Count);
    }

    [Fact]
    public void RulesVersionEventIsFilteredClientSide()
    {
        var fixture = JsonNode.Parse("""{"event":{"data":{"kind":"rules","version":9}}}""")!;
        Assert.True(ProtocolCodec.IsRulesVersionEvent(fixture));
    }

    [Fact]
    public void ClassificationAssignmentUsesResponseContract()
    {
        var json = JsonNode.Parse(ProtocolCodec.SetClassification(
            4, "alex", "domain", "example.com", "schoolwork"))!;
        Assert.Equal("set_classification", json["service"]!.GetValue<string>());
        Assert.True(json["return_response"]!.GetValue<bool>());
        Assert.Equal("example.com", json["service_data"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void AggregateStatusDoesNotDependOnEntityIds()
    {
        var json = JsonNode.Parse(ProtocolCodec.GetStatus(8, "alex", "pc"))!;
        Assert.Equal("get_status", json["service"]!.GetValue<string>());
        Assert.True(json["return_response"]!.GetValue<bool>());
        Assert.Equal("pc", json["service_data"]!["device"]!.GetValue<string>());
    }
}
