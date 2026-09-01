using Gnomon.Core;

namespace Gnomon.Core.Tests;

public class AgentConfigurationTests
{
    [Theory]
    [InlineData("homeassistant.local", "ws://homeassistant.local:8123/api/websocket")]
    [InlineData("homeassistant.local:8123", "ws://homeassistant.local:8123/api/websocket")]
    [InlineData("http://ha.lan:8123", "ws://ha.lan:8123/api/websocket")]
    [InlineData("https://ha.example.com", "wss://ha.example.com/api/websocket")]
    [InlineData("wss://ha.example.com/custom", "wss://ha.example.com/api/websocket")]
    public void HomeAssistantAddressIsNormalized(string input, string expected)
    {
        Assert.True(AgentConfiguration.TryNormalizeHomeAssistantUrl(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///tmp/home-assistant")]
    [InlineData("not a host")]
    public void InvalidHomeAssistantAddressIsRejected(string input)
    {
        Assert.False(AgentConfiguration.TryNormalizeHomeAssistantUrl(input, out _));
    }
}
