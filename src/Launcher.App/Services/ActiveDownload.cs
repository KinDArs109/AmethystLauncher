using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Download;

namespace Launcher.App.Services;

/// <summary>UI-facing mirror of an <see cref="InstallProgress"/>, ticked by <see cref="DownloadCenter"/>.</summary>
public sealed partial class ActiveDownload : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required InstallProgress Progress { get; init; }

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private long _downloadedBytes;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string? _errorMessage;
}
