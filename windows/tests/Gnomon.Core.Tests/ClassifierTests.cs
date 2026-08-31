using Gnomon.Core;

namespace Gnomon.Core.Tests;

public class ClassifierTests
{
    private static RulesMap Rules => new(7,
        [new("games", "Games"), new("video", "Video", 3, true), new("schoolwork", "Schoolwork")],
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["game.exe"] = "games" },
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["youtube.com"] = "video" },
        new Dictionary<string, RuleOverrides>(StringComparer.OrdinalIgnoreCase)
        {
            ["alex"] = new(new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["khanacademy.org"] = "schoolwork"
            })
        });

    [Fact]
    public void ExactProcessAndKidOverrideAreResolved()
    {
        var classifier = new Classifier();
        Assert.Equal("games", classifier.Classify("GAME.EXE", "alex", null, null, DateTimeOffset.UtcNow, Rules).Category);
        Assert.Equal("schoolwork", classifier.Classify("msedge.exe", "alex", "www.khanacademy.org",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Rules).Category);
    }

    [Fact]
    public void BrowserSuffixAndStaleExtensionAreHandled()
    {
        var now = DateTimeOffset.UtcNow;
        var classifier = new Classifier();
        Assert.Equal("video", classifier.Classify("msedge.exe", "alex", "www.youtube.com", now, now, Rules).Category);
        Assert.True(classifier.Classify("msedge.exe", "alex", "youtube.com", now.AddSeconds(-61), now, Rules).IsUnknown);
    }
}
