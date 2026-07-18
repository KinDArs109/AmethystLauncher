using Launcher.Core.Instances;
using Launcher.Core.Launch;
using Launcher.Core.Versions;
using Launcher.Loaders.Abstractions;

namespace Launcher.App.Services;

/// <summary>
/// A <see cref="LauncherInstance"/> only persists version/loader identifiers, not the resolved
/// <see cref="VersionDetails"/> — this re-derives them (vanilla manifest lookup, plus the loader's own
/// merge if the instance isn't Vanilla) for both the post-creation download and every "Играть" launch.
/// </summary>
public interface IInstanceVersionResolver
{
    Task<VersionDetails> ResolveAsync(LauncherInstance instance, Action<string>? onStatus = null, CancellationToken ct = default);
}

public sealed class InstanceVersionResolver(
    IVersionManifestService versionManifestService,
    IEnumerable<ILoaderInstaller> loaderInstallers) : IInstanceVersionResolver
{
    public async Task<VersionDetails> ResolveAsync(LauncherInstance instance, Action<string>? onStatus = null, CancellationToken ct = default)
    {
        onStatus?.Invoke("Загрузка сведений о версии...");
        var manifest = await versionManifestService.GetManifestAsync(ct);
        var entry = manifest.Versions.FirstOrDefault(v => v.Id == instance.VersionId)
            ?? throw new InvalidOperationException($"Версия '{instance.VersionId}' не найдена в манифесте Mojang.");
        var vanillaVersion = await versionManifestService.GetVersionDetailsAsync(entry, ct);

        if (instance.LoaderType == "Vanilla")
        {
            return vanillaVersion;
        }

        if (!Enum.TryParse<ModLoaderType>(instance.LoaderType, out var loaderType) || instance.LoaderVersion is null)
        {
            throw new InvalidOperationException($"У сборки '{instance.Name}' некорректные данные загрузчика.");
        }

        var installer = loaderInstallers.FirstOrDefault(l => l.LoaderType == loaderType)
            ?? throw new InvalidOperationException($"Нет установщика для загрузчика '{instance.LoaderType}'.");

        var statusAdapter = onStatus is null ? null : new Progress<string>(onStatus);
        return await installer.InstallAsync(
            instance.VersionId, instance.LoaderVersion, vanillaVersion, InstancePreparer.GetSharedGameRoot(), statusAdapter, ct);
    }
}
