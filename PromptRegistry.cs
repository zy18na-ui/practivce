// dataAccess/Planning/PromptRegistry.cs
namespace dataAccess.Planning;

public sealed class PromptRegistry
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["expense"] = "reports.expense.yaml",
        ["sales"] = "reports.sales.yaml",
        ["inventory"] = "reports.inventory.yaml",
        ["chitchat"] = "chitchat.yaml"
    };

    public bool TryResolve(string key, out string file) => _map.TryGetValue(key, out file!);

    public string Resolve(string key)
        => _map.TryGetValue(key, out var file)
           ? file
           : throw new KeyNotFoundException($"No YAML mapped for '{key}'");
}
