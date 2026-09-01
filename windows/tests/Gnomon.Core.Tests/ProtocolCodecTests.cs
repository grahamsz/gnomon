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
        Assert.Equal("process", json["service_data"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void RulesVersionEventIsFilteredClientSide()
    {
        var fixture = JsonNode.Parse("""{"event":{"data":{"entity_id":"sensor.gnomon_rules_version"}}}""")!;
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
}
