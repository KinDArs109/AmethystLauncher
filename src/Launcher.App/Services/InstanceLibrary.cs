using System.Collections.ObjectModel;
using System.Windows.Data;
using Launcher.Core.Instances;

namespace Launcher.App.Services;

/// <summary>
/// App-wide, always-current view of created instances. <see cref="IInstanceManager"/> itself is just
/// file I/O with no in-memory state, so a instance created from the global "+" button (which isn't tied
/// to whichever page happens to be open) wouldn't otherwise be visible on the Библиотека page without this
/// shared collection sitting between them.
/// </summary>
public interface IInstanceLibrary
{
    ObservableCollection<LauncherInstance> Instances { get; }

    Task RefreshAsync(CancellationToken ct = default);

    Task<LauncherInstance> CreateAsync(
        string name, string versionId, string loaderType, string? loaderVersion, CancellationToken ct = default);

    Task DeleteAsync(LauncherInstance instance, CancellationToken ct = default);

    Task<LauncherInstance> RenameAsync(LauncherInstance instance, string newName, CancellationToken ct = default);

    Task<LauncherInstance> MarkPlayedAsync(LauncherInstance instance, CancellationToken ct = default);

    Task<LauncherInstance> UpdateAsync(LauncherInstance instance, CancellationToken ct = default);

    Task<LauncherInstance> CloneAsync(LauncherInstance instance, string newName, CancellationToken ct = default);
}

/// <summary>
/// Some callers (e.g. a modpack install, which downloads/prepares a whole instance on a background
/// thread via <see cref="Task.Run(Action)"/>) create instances off the UI thread. <see cref="Instances"/>
/// is bound directly into WPF views, and CollectionView normally rejects any SourceCollection mutation
/// that didn't come from the Dispatcher thread — EnableCollectionSynchronization lets WPF take a lock
/// instead of throwing, so background-thread Add/Remove just works.
/// </summary>
public sealed class InstanceLibrary : IInstanceLibrary
{
    private readonly IInstanceManager _instanceManager;
    private readonly object _syncLock = new();

    public ObservableCollection<LauncherInstance> Instances { get; } = [];

    public InstanceLibrary(IInstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
        BindingOperations.EnableCollectionSynchronization(Instances, _syncLock);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var all = await _instanceManager.GetInstancesAsync(ct);
        Instances.Clear();
        foreach (var instance in all)
        {
            Instances.Add(instance);
        }
    }

    public async Task<LauncherInstance> CreateAsync(
        string name, string versionId, string loaderType, string? loaderVersion, CancellationToken ct = default)
    {
        var instance = await _instanceManager.GetOrCreateInstanceAsync(name, versionId, loaderType, loaderVersion, ct);
        if (Instances.All(i => i.DirectoryPath != instance.DirectoryPath))
        {
            Instances.Add(instance);
        }

        return instance;
    }

    public async Task DeleteAsync(LauncherInstance instance, CancellationToken ct = default)
    {
        await _instanceManager.DeleteInstanceAsync(instance, ct);
        var existing = Instances.FirstOrDefault(i => i.DirectoryPath == instance.DirectoryPath);
        if (existing is not null)
        {
            Instances.Remove(existing);
        }
    }

    public async Task<LauncherInstance> RenameAsync(LauncherInstance instance, string newName, CancellationToken ct = default)
    {
        var renamed = await _instanceManager.RenameInstanceAsync(instance, newName, ct);
        ReplaceInPlace(instance, renamed);
        return renamed;
    }

    public async Task<LauncherInstance> MarkPlayedAsync(LauncherInstance instance, CancellationToken ct = default)
    {
        var updated = await _instanceManager.MarkPlayedAsync(instance, ct);
        ReplaceInPlace(instance, updated);
        return updated;
    }

    public async Task<LauncherInstance> UpdateAsync(LauncherInstance instance, CancellationToken ct = default)
    {
        var updated = await _instanceManager.UpdateAsync(instance, ct);
        ReplaceInPlace(instance, updated);
        return updated;
    }

    public async Task<LauncherInstance> CloneAsync(LauncherInstance instance, string newName, CancellationToken ct = default)
    {
        var clone = await _instanceManager.CloneInstanceAsync(instance, newName, ct);
        Instances.Add(clone);
        return clone;
    }

    private void ReplaceInPlace(LauncherInstance oldInstance, LauncherInstance newInstance)
    {
        var index = Instances.ToList().FindIndex(i => i.DirectoryPath == oldInstance.DirectoryPath);
        if (index >= 0)
        {
            Instances[index] = newInstance;
        }
    }
}
