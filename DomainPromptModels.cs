// dataAccess/Planning/DomainPromptModels.cs
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dataAccess.Planning;

public sealed class DomainPrompt
{
    public string? Name { get; init; }
    public ModelOverride? Model { get; init; }
    public PhaseBlock? Phase1 { get; init; }
    public PhaseBlock? Phase2 { get; init; }
    public Meta? Metadata { get; init; }

    public sealed class ModelOverride
    {
        public string? Name { get; init; }
        public double? Temperature { get; init; }
        public int? Max_Output_Tokens { get; init; }
    }

    public sealed class PhaseBlock
    {
        public string? System { get; init; }
        public List<FewShot>? Few_Shot { get; init; } // optional
        public string? Fix_Format_Instruction { get; init; } // optional
    }

    public sealed class FewShot { public string? User { get; init; } public string? Output { get; init; } }

    public sealed class Meta
    {
        public List<string>? Sql_Catalog_Required { get; init; }
        public Dictionary<string, object>? Validation { get; init; }
        public Dictionary<string, string>? Api_Surface { get; init; }
    }

    // --- loader helper
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static DomainPrompt Parse(string yaml)
        => Deserializer.Deserialize<DomainPrompt>(yaml)
           ?? throw new InvalidOperationException("Domain prompt parsed to null");
}
