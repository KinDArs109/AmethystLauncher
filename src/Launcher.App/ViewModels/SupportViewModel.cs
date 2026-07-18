using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Backend.Models;
using Launcher.Backend.Support;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace Launcher.App.ViewModels;

public partial class SupportViewModel : ObservableObject
{
    private readonly ISupportService _supportService;
    private readonly IContentDialogService _contentDialogService;
    private readonly ILogger<SupportViewModel> _logger;
    private bool _subscribed;

    // Backing lists for both views — split once when threads load, kept in sync as threads are
    // closed/deleted. "Threads" is what the left ListBox actually shows (swapped on IsHistoryView).
    private readonly List<SupportThread> _activeThreads = [];
    private readonly List<SupportThread> _closedThreads = [];

    public ObservableCollection<SupportThread> Threads { get; } = [];
    public ObservableCollection<SupportMessage> Messages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedThread))]
    [NotifyPropertyChangedFor(nameof(CanCloseSelectedThread))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseThreadCommand))]
    private SupportThread? _selectedThread;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateThreadCommand))]
    private string _newThreadSubject = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _messageText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateThreadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Toggled by the "История тем" / "Назад" button — swaps <see cref="Threads"/> between the
    /// active list and closed (history) list without a separate page/navigation.</summary>
    [ObservableProperty]
    private bool _isHistoryView;

    public bool HasSelectedThread => SelectedThread is not null;

    /// <summary>"Завершить диалог" is only offered for a thread that isn't already closed — once closed
    /// (by the player here or the admin from Telegram), it belongs in "История тем" instead.</summary>
    public bool CanCloseSelectedThread => SelectedThread is { Status: not "closed" };

    public SupportViewModel(ISupportService supportService, IContentDialogService contentDialogService, ILogger<SupportViewModel> logger)
    {
        _supportService = supportService;
        _contentDialogService = contentDialogService;
        _logger = logger;
        LoadThreadsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadThreadsAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "";

            var threads = await _supportService.GetMyThreadsAsync();
            _activeThreads.Clear();
            _closedThreads.Clear();
            foreach (var thread in threads)
            {
                (thread.Status == "closed" ? _closedThreads : _activeThreads).Add(thread);
            }

            RefreshVisibleThreads();
            SelectedThread ??= Threads.FirstOrDefault();

            if (!_subscribed)
            {
                _subscribed = true;
                await _supportService.SubscribeToMessagesAsync(OnNewMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load support threads");
            StatusText = "Не удалось загрузить обращения — проверьте подключение.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsHistoryViewChanged(bool value)
    {
        RefreshVisibleThreads();
        SelectedThread = Threads.FirstOrDefault();
    }

    [RelayCommand]
    private void ToggleHistoryView() => IsHistoryView = !IsHistoryView;

    private void RefreshVisibleThreads()
    {
        var source = IsHistoryView ? _closedThreads : _activeThreads;
        Threads.Clear();
        foreach (var thread in source)
        {
            Threads.Add(thread);
        }
    }

    partial void OnSelectedThreadChanged(SupportThread? value)
    {
        Messages.Clear();
        if (value is not null)
        {
            _ = LoadMessagesAsync(value.Id);
        }
    }

    private async Task LoadMessagesAsync(Guid threadId)
    {
        try
        {
            var messages = await _supportService.GetMessagesAsync(threadId);
            if (SelectedThread?.Id != threadId)
            {
                return;
            }

            Messages.Clear();
            foreach (var message in messages)
            {
                Messages.Add(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load support messages for thread {ThreadId}", threadId);
            StatusText = "Не удалось загрузить переписку.";
        }
    }

    private bool CanCreateThread => !IsBusy && !string.IsNullOrWhiteSpace(NewThreadSubject);

    [RelayCommand(CanExecute = nameof(CanCreateThread))]
    private async Task CreateThreadAsync()
    {
        try
        {
            IsBusy = true;
            var subject = NewThreadSubject.Trim();
            var thread = await _supportService.CreateThreadAsync(subject);
            NewThreadSubject = "";
            _activeThreads.Insert(0, thread);
            if (!IsHistoryView)
            {
                Threads.Insert(0, thread);
            }

            SelectedThread = thread;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create support thread");
            StatusText = "Не удалось создать обращение.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSendMessage => !IsBusy && SelectedThread is not null && !string.IsNullOrWhiteSpace(MessageText);

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (SelectedThread is null)
        {
            return;
        }

        var thread = SelectedThread;
        var body = MessageText.Trim();
        MessageText = "";

        try
        {
            await _supportService.SendMessageAsync(thread.Id, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send support message");
            StatusText = "Не удалось отправить сообщение.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCloseSelectedThread))]
    private async Task CloseThreadAsync()
    {
        if (SelectedThread is not { Status: not "closed" } thread)
        {
            return;
        }

        try
        {
            await _supportService.CloseThreadAsync(thread.Id);

            thread.Status = "closed";
            _activeThreads.Remove(thread);
            _closedThreads.Insert(0, thread);
            Threads.Remove(thread);
            SelectedThread = Threads.FirstOrDefault();
            StatusText = "Обращение завершено — оно теперь в «Истории тем».";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close support thread {ThreadId}", thread.Id);
            StatusText = "Не удалось завершить диалог.";
        }
    }

    [RelayCommand]
    private async Task DeleteThreadAsync(SupportThread thread)
    {
        var confirmed = await DialogHelpers.ConfirmAsync(
            _contentDialogService, "Удалить обращение?", $"«{thread.Subject}» и вся переписка будут удалены безвозвратно.");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _supportService.DeleteThreadAsync(thread.Id);

            _activeThreads.Remove(thread);
            _closedThreads.Remove(thread);
            Threads.Remove(thread);
            if (SelectedThread == thread)
            {
                SelectedThread = Threads.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete support thread {ThreadId}", thread.Id);
            StatusText = "Не удалось удалить обращение.";
        }
    }

    private void OnNewMessage(SupportMessage message)
    {
        // Realtime callbacks fire on a background thread; ObservableCollection mutation must happen on the UI thread.
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (SelectedThread is not null
                && message.ThreadId == SelectedThread.Id
                && Messages.All(m => m.Id != message.Id))
            {
                Messages.Add(message);
            }
        });
    }
}
