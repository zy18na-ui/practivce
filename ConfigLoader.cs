// dataAccess/Planning/ConfigLoader.cs
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dataAccess.Planning;

public static class ConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static BConfig Load(PromptLoader loader, string fileName = "config.yaml")
    {
        var yaml = loader.ReadText(fileName);
        var cfg = Deserializer.Deserialize<BConfig>(yaml)
                  ?? throw new InvalidOperationException("config.yaml parsed to null");
        // quick sanity
        if (string.IsNullOrWhiteSpace(cfg.identity?.name)) throw new InvalidOperationException("identity.name missing");
        if (cfg.defaults is null) throw new InvalidOperationException("defaults missing");
        return cfg;
    }
}
