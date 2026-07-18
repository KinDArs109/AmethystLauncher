using System.Text.Json;
using Launcher.Core.Versions;

namespace Launcher.Core.Launch;

public interface IArgumentBuilder
{
    List<string> BuildJvmArguments(LaunchContext context, int minRamMb, int maxRamMb, string? extraJvmArgs = null);

    List<string> BuildGameArguments(LaunchContext context, int? windowWidth = null, int? windowHeight = null);
}

public sealed class ArgumentBuilder : IArgumentBuilder
{
    public List<string> BuildJvmArguments(LaunchContext context, int minRamMb, int maxRamMb, string? extraJvmArgs = null)
    {
        var args = new List<string> { $"-Xms{minRamMb}M", $"-Xmx{maxRamMb}M" };

        if (context.Version.Arguments?.Jvm is { Count: > 0 } modernJvm)
        {
            args.AddRange(ResolveModernArguments(modernJvm, context));
        }
        else
        {
            // Pre-1.13 versions don't declare JVM arguments in their version JSON; the launcher must supply
            // the bare minimum itself (classpath + native library path) for the JVM to find anything.
            args.Add($"-Djava.library.path={context.NativesDirectory}");
            args.Add("-cp");
            args.Add(context.Classpath);
        }

        if (!string.IsNullOrWhiteSpace(extraJvmArgs))
        {
            args.AddRange(extraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return args;
    }

    public List<string> BuildGameArguments(LaunchContext context, int? windowWidth = null, int? windowHeight = null)
    {
        List<string> args;
        if (context.Version.Arguments?.Game is { Count: > 0 } modernGame)
        {
            args = ResolveModernArguments(modernGame, context);
        }
        else if (!string.IsNullOrEmpty(context.Version.MinecraftArguments))
        {
            args = SubstitutePlaceholders(context.Version.MinecraftArguments, context)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
        else
        {
            throw new InvalidOperationException($"Version '{context.Version.Id}' declares neither modern nor legacy launch arguments.");
        }

        if (windowWidth is > 0 && windowHeight is > 0)
        {
            args.Add("--width");
            args.Add(windowWidth.Value.ToString());
            args.Add("--height");
            args.Add(windowHeight.Value.ToString());
        }

        return args;
    }

    private static List<string> ResolveModernArguments(List<JsonElement> elements, LaunchContext context)
    {
        var result = new List<string>();

        foreach (var element in elements)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                result.Add(SubstitutePlaceholders(element.GetString()!, context));
                continue;
            }

            if (element.TryGetProperty("rules", out var rulesElement))
            {
                var rules = rulesElement.Deserialize<List<Rule>>() ?? [];
                if (!RuleEvaluator.IsAllowed(rules))
                {
                    continue;
                }
            }

            if (!element.TryGetProperty("value", out var valueElement))
            {
                continue;
            }

            if (valueElement.ValueKind == JsonValueKind.String)
            {
                result.Add(SubstitutePlaceholders(valueElement.GetString()!, context));
            }
            else if (valueElement.ValueKind == JsonValueKind.Array)
            {
                result.AddRange(valueElement.EnumerateArray().Select(v => SubstitutePlaceholders(v.GetString()!, context)));
            }
        }

        return result;
    }

    private static string SubstitutePlaceholders(string template, LaunchContext context) => template
        .Replace("${auth_player_name}", context.PlayerName)
        .Replace("${version_name}", context.Version.Id)
        .Replace("${game_directory}", context.GameDirectory)
        .Replace("${assets_root}", context.AssetsRoot)
        .Replace("${assets_index_name}", context.Version.AssetIndex.Id)
        .Replace("${auth_uuid}", context.PlayerUuid.ToString("N"))
        .Replace("${auth_access_token}", context.AccessToken)
        .Replace("${user_type}", context.UserType)
        .Replace("${version_type}", context.Version.Type)
        .Replace("${natives_directory}", context.NativesDirectory)
        .Replace("${launcher_name}", "MinecraftLauncher")
        .Replace("${launcher_version}", "0.1.0")
        .Replace("${classpath}", context.Classpath)
        .Replace("${clientid}", "")
        .Replace("${auth_xuid}", "");
}
