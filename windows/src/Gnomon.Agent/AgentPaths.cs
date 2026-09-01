namespace Gnomon.Agent;

public sealed record AgentPaths(
    string DataDirectory, string LogDirectory, string ConfigFile,
    string RulesCacheFile, string ActivityFile)
{
    public static AgentPaths Create()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Gnomon");
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gnomon");
        Directory.CreateDirectory(local);
        return new AgentPaths(root, Path.Combine(root, "logs"), Path.Combine(root, "config.json"),
            Path.Combine(root, "rules-cache.json"), Path.Combine(local, "activity.json"));
    }
}
