namespace Launcher.Core.Versions;

/// <summary>Evaluates Mojang's library/argument <see cref="Rule"/> lists for a Windows-only launcher.</summary>
public static class RuleEvaluator
{
    public static bool IsAllowed(IReadOnlyList<Rule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!Matches(rule))
            {
                continue;
            }

            allowed = rule.Action == "allow";
        }

        return allowed;
    }

    private static bool Matches(Rule rule)
    {
        if (rule.Os is { Name: not null } os && os.Name != "windows")
        {
            return false;
        }

        // Optional features (demo mode, custom resolution, quick play, ...) aren't supported yet;
        // any rule gated on one never matches, so those arguments are simply omitted.
        if (rule.Features is { Count: > 0 })
        {
            return false;
        }

        return true;
    }
}
