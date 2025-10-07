// dataAccess/Planning/PromptLoader.cs
using System.Text;

namespace dataAccess.Planning;

public sealed class PromptLoader
{
    private readonly string _dir;
    public PromptLoader(string? dir = null)
        => _dir = dir ?? Path.Combine(AppContext.BaseDirectory, "Planning", "Prompts");

    public string ReadText(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        if (!File.Exists(path)) throw new FileNotFoundException($"Prompt not found: {path}");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public bool Exists(string fileName) => File.Exists(Path.Combine(_dir, fileName));

    public IReadOnlyDictionary<string, string> ReadAll()
    {
        if (!Directory.Exists(_dir)) return new Dictionary<string, string>();
        return Directory.EnumerateFiles(_dir, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(Path.GetFileName, File.ReadAllText);
    }

    public string Root => _dir;
}
