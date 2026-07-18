using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Backend.Friends;
using Launcher.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels;

public sealed class FriendListItem(string username, bool isOnline, string? playingInstance)
{
    public string Username { get; } = username;
    public bool IsOnline { get; } = isOnline;
    public string Initial { get; } = username.Length > 0 ? username[..1].ToUpperInvariant() : "?";

    public string StatusText { get; } = playingInstance is not null
        ? $"Играет в {playingInstance}"
        : isOnline ? "В сети" : "Не в сети";
}

/// <summary>Backs the docked "Друзья" card in the right column: add-by-nickname, incoming requests,
/// and the friend list with live presence. Singleton — the panel is visible on every page, and the
/// 30-second refresh loop should run exactly once for the whole app.</summary>
public partial class FriendsPanelViewModel : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IFriendsService _friendsService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<FriendsPanelViewModel> _logger;

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFriendCommand))]
    private string _addFriendText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<FriendListItem> Friends { get; } = [];

    public ObservableCollection<FriendRequest> Requests { get; } = [];

    public FriendsPanelViewModel(
        IFriendsService friendsService,
        ISettingsService settingsService,
        ILogger<FriendsPanelViewModel> logger)
    {
        _friendsService = friendsService;
        _settingsService = settingsService;
        _logger = logger;

        // Constructed on the UI thread, so every continuation in this loop lands back on it —
        // safe to touch the ObservableCollections without explicit dispatching.
        _ = RunRefreshLoopAsync();
    }

    private async Task RunRefreshLoopAsync()
    {
        while (true)
        {
            await RefreshAsync();
            await Task.Delay(RefreshInterval);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            var username = settings.LocalAccountUsername;
            IsSignedIn = !string.IsNullOrWhiteSpace(username);
            if (!IsSignedIn)
            {
                Friends.Clear();
                Requests.Clear();
                return;
            }

            var friends = await _friendsService.GetFriendsAsync(username!);
            var requests = await _friendsService.GetRequestsAsync(username!);

            Friends.Clear();
            foreach (var f in friends)
            {
                Friends.Add(new FriendListItem(f.Username, f.IsOnline, f.PlayingInstance));
            }

            Requests.Clear();
            foreach (var r in requests)
            {
                Requests.Add(r);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Friends panel refresh failed");
        }
    }

    private bool CanAddFriend => !string.IsNullOrWhiteSpace(AddFriendText);

    [RelayCommand(CanExecute = nameof(CanAddFriend))]
    private async Task AddFriendAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "";

            var settings = await _settingsService.LoadAsync();
            if (string.IsNullOrWhiteSpace(settings.LocalAccountUsername))
            {
                StatusText = "Сначала войдите в аккаунт.";
                return;
            }

            var accepted = await _friendsService.SendRequestAsync(settings.LocalAccountUsername, AddFriendText.Trim());
            StatusText = accepted ? "Теперь вы друзья!" : "Заявка отправлена.";
            AddFriendText = "";
            await RefreshAsync();
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send friend request");
            StatusText = "Не удалось отправить заявку. Проверьте интернет.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AcceptRequestAsync(FriendRequest request) => await RespondAsync(request, accept: true);

    [RelayCommand]
    private async Task DeclineRequestAsync(FriendRequest request) => await RespondAsync(request, accept: false);

    private async Task RespondAsync(FriendRequest request, bool accept)
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            if (string.IsNullOrWhiteSpace(settings.LocalAccountUsername))
            {
                return;
            }

            await _friendsService.RespondAsync(settings.LocalAccountUsername, request.Requester, accept);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to respond to friend request from {Requester}", request.Requester);
            StatusText = "Не удалось обработать заявку.";
        }
    }

    [RelayCommand]
    private async Task RemoveFriendAsync(FriendListItem friend)
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            if (string.IsNullOrWhiteSpace(settings.LocalAccountUsername))
            {
                return;
            }

            await _friendsService.RemoveAsync(settings.LocalAccountUsername, friend.Username);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove friend {Friend}", friend.Username);
            StatusText = "Не удалось удалить из друзей.";
        }
    }
}
