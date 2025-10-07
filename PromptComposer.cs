// dataAccess/Planning/PromptComposer.cs
namespace dataAccess.Planning;

public sealed class PromptComposer(BConfig cfg, PromptLoader loader)
{
    public (string system, string? fix) ComposePhase1(string domainFile)
    {
        var dp = DomainPrompt.Parse(loader.ReadText(domainFile));
        var identity = IdentityHeader();
        var sys = Required(dp?.Phase1?.System, domainFile, "phase1.system");
        return (identity + sys, dp?.Phase1?.Fix_Format_Instruction);
    }

    public (string system, string? fix) ComposePhase2(string domainFile)
    {
        var dp = DomainPrompt.Parse(loader.ReadText(domainFile));
        var identity = IdentityHeader();
        var sys = Required(dp?.Phase2?.System, domainFile, "phase2.system");
        return (identity + sys, dp?.Phase2?.Fix_Format_Instruction);
    }

    public IEnumerable<(string user, string output)> FewShots(string domainFile, int phase = 1)
    {
        var dp = DomainPrompt.Parse(loader.ReadText(domainFile));
        var shots = (phase == 1 ? dp?.Phase1?.Few_Shot : dp?.Phase2?.Few_Shot) ?? new();
        foreach (var s in shots.Where(s => s.User is not null && s.Output is not null))
            yield return (s.User!, s.Output!);
    }

    private string IdentityHeader()
        => $"You are \"{cfg.identity.name}\". {cfg.identity.role}\n";

    private static string Required(string? value, string file, string key)
        => !string.IsNullOrWhiteSpace(value) ? value!
           : throw new InvalidOperationException($"{file} missing {key}");
}
