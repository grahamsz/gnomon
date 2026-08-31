using Gnomon.Core;

namespace Gnomon.Core.Tests;

public class AccountingTests
{
    [Fact]
    public void ActivityFormulaMatchesSpec()
    {
        Assert.False(ActivityStateMachine.IsCounting(new(true, true, true, false, false)));
        Assert.True(ActivityStateMachine.IsCounting(new(true, true, true, true, true)));
        Assert.False(ActivityStateMachine.IsCounting(new(true, false, false, true, true)));
    }

    [Fact]
    public void QuantizerKeepsFractionalRemainder()
    {
        var quantizer = new DeltaQuantizer();
        quantizer.Accumulate("games", TimeSpan.FromSeconds(125));
        Assert.Equal(2, quantizer.FlushWholeMinutes("games"));
        Assert.Equal(5, quantizer.RemainderSeconds("games"));
    }
}
