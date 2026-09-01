namespace Gnomon.Agent;

public sealed record AgentPaths(string DataDirectory, string LogDirectory, string ConfigFile, string RulesCacheFile)
{
    public static AgentPaths Create()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Gnomon");
        return new AgentPaths(root, Path.Combine(root, "logs"), Path.Combine(root, "config.json"),
            Path.Combine(root, "rules-cache.json"));
    }
}
