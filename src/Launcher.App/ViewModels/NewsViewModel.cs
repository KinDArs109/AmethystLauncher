using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Backend.Models;
using Launcher.Backend.News;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels;

public partial class NewsViewModel : ObservableObject
{
    private readonly IAnnouncementsService _announcementsService;
    private readonly ILogger<NewsViewModel> _logger;
    private readonly List<Announcement> _all = [];
    private bool _subscribed;

    public ObservableCollection<Announcement> Announcements { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "";

    public NewsViewModel(IAnnouncementsService announcementsService, ILogger<NewsViewModel> logger)
    {
        _announcementsService = announcementsService;
        _logger = logger;
        LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "";

            var items = await _announcementsService.GetPublishedAsync();
            _all.Clear();
            _all.AddRange(items);
            RefreshView();

            // Realtime is a nice-to-have: if the websocket subscription fails, the list above still
            // loaded fine, so don't scare the user with a connection error — just log it.
            if (!_subscribed)
            {
                try
                {
                    await _announcementsService.SubscribeAsync(OnAnnouncementChanged);
                    _subscribed = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Realtime subscription for announcements failed; updates need a page revisit");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load announcements");
            StatusText = "Не удалось загрузить новости — проверьте подключение.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnAnnouncementChanged(Announcement announcement)
    {
        // Realtime callbacks fire on a background thread; ObservableCollection mutation must happen on the UI thread.
        Application.Current.Dispatcher.Invoke(() =>
        {
            _all.RemoveAll(a => a.Id == announcement.Id);
            if (announcement.PublishedAt is not null)
            {
                _all.Add(announcement);
            }

            RefreshView();
        });
    }

    private void RefreshView()
    {
        var sorted = _all.OrderByDescending(a => a.Pinned).ThenByDescending(a => a.CreatedAt);
        Announcements.Clear();
        foreach (var item in sorted)
        {
            Announcements.Add(item);
        }
    }
}
