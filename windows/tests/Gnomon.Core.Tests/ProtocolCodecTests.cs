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
    }

    [Fact]
    public void RulesVersionEventIsFilteredClientSide()
    {
        var fixture = JsonNode.Parse("""{"event":{"data":{"entity_id":"sensor.gnomon_rules_version"}}}""")!;
        Assert.True(ProtocolCodec.IsRulesVersionEvent(fixture));
    }
}
