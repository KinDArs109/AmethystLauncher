using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Launcher.App.Messages;
using Launcher.App.Services;
using Launcher.Backend.Accounts;
using Launcher.Backend.Friends;
using Launcher.Backend.Skins;
using Launcher.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly ILocalAccountStore _localAccountStore;
    private readonly ISettingsService _settingsService;
    private readonly PresenceService _presenceService;
    private readonly IFriendsService _friendsService;
    private readonly ISkinService _skinService;
    private readonly ILogger<AccountsViewModel> _logger;

    [ObservableProperty]
    private string _nickname = "Player";

    // ---- Local (username+password) account ----

    [ObservableProperty]
    private bool _isLocalSignedIn;

    [ObservableProperty]
    private string? _localSignedInUsername;

    [ObservableProperty]
    private bool _isRegisterMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LocalSignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(LocalRegisterCommand))]
    private string _localUsername = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LocalSignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(LocalRegisterCommand))]
    private string _localPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LocalRegisterCommand))]
    private string _localConfirmPassword = "";

    [ObservableProperty]
    private bool _isLocalBusy;

    [ObservableProperty]
    private string _localStatusText = "";

    public AccountsViewModel(
        ILocalAccountStore localAccountStore,
        ISettingsService settingsService,
        PresenceService presenceService,
        IFriendsService friendsService,
        ISkinService skinService,
        ILogger<AccountsViewModel> logger)
    {
        _localAccountStore = localAccountStore;
        _settingsService = settingsService;
        _presenceService = presenceService;
        _friendsService = friendsService;
        _skinService = skinService;
        _logger = logger;

        LoadActiveProfileCommand.ExecuteAsync(null);
        LoadLocalSessionCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadLocalSessionAsync()
    {
        var settings = await _settingsService.LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.LocalAccountUsername))
        {
            return;
        }

        IsLocalSignedIn = true;
        LocalSignedInUsername = settings.LocalAccountUsername;
        await LoadSkinAsync();
    }

    // ---- Skin ----

    [ObservableProperty]
    private bool _isSkinSlim;

    [ObservableProperty]
    private ImageSource? _skinHeadPreview;

    [ObservableProperty]
    private bool _hasSkin;

    [ObservableProperty]
    private bool _isSkinBusy;

    [ObservableProperty]
    private string _skinStatusText = "";

    private byte[]? _skinBytes;
    private bool _suppressSkinModelUpload;

    private async Task LoadSkinAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalSignedInUsername))
        {
            return;
        }

        try
        {
            var skin = await _skinService.GetAsync(LocalSignedInUsername);
            if (skin is null)
            {
                HasSkin = false;
                SkinHeadPreview = null;
                _skinBytes = null;
                return;
            }

            _skinBytes = skin.PngBytes;
            _suppressSkinModelUpload = true;
            IsSkinSlim = skin.Model == "slim";
            _suppressSkinModelUpload = false;
            SkinHeadPreview = BuildHeadPreview(_skinBytes);
            HasSkin = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load skin");
        }
    }

    /// <summary>The 8x8 face region of the skin texture, shown pixelated as an avatar.</summary>
    private static ImageSource? BuildHeadPreview(byte[] pngBytes)
    {
        try
        {
            using var stream = new MemoryStream(pngBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            var head = new CroppedBitmap(bitmap, new Int32Rect(8, 8, 8, 8));
            head.Freeze();
            return head;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task UploadSkinAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalSignedInUsername))
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите файл скина",
            Filter = "Скин Minecraft (*.png)|*.png",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsSkinBusy = true;
            SkinStatusText = "";

            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            using (var stream = new MemoryStream(bytes))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                if (frame.PixelWidth != 64 || (frame.PixelHeight != 64 && frame.PixelHeight != 32))
                {
                    SkinStatusText = "Скин должен быть PNG размером 64×64 (или старый формат 64×32).";
                    return;
                }
            }

            await _skinService.UploadAsync(LocalSignedInUsername, bytes, IsSkinSlim ? "slim" : "default");
            _skinBytes = bytes;
            SkinHeadPreview = BuildHeadPreview(bytes);
            HasSkin = true;
            SkinStatusText = "Скин загружен! Он появится в игре при следующем запуске.";
        }
        catch (InvalidOperationException ex)
        {
            SkinStatusText = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skin upload failed");
            SkinStatusText = "Не удалось загрузить скин. Проверьте интернет.";
        }
        finally
        {
            IsSkinBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSkinAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalSignedInUsername))
        {
            return;
        }

        try
        {
            IsSkinBusy = true;
            await _skinService.DeleteAsync(LocalSignedInUsername);
            HasSkin = false;
            SkinHeadPreview = null;
            _skinBytes = null;
            SkinStatusText = "Скин удалён — в игре вернётся стандартный.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skin delete failed");
            SkinStatusText = "Не удалось удалить скин.";
        }
        finally
        {
            IsSkinBusy = false;
        }
    }

    /// <summary>Switching Steve/Alex arms re-uploads the stored skin with the new model so the change
    /// takes effect in-game without asking the user to pick the file again.</summary>
    partial void OnIsSkinSlimChanged(bool value)
    {
        if (_suppressSkinModelUpload || _skinBytes is null || string.IsNullOrWhiteSpace(LocalSignedInUsername))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _skinService.UploadAsync(LocalSignedInUsername, _skinBytes, value ? "slim" : "default");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skin model re-upload failed");
            }
        });
    }

    [RelayCommand]
    private void ToggleAuthMode() => IsRegisterMode = !IsRegisterMode;

    private bool CanSubmitLocalAuth => !IsLocalBusy && !string.IsNullOrWhiteSpace(LocalUsername) && !string.IsNullOrWhiteSpace(LocalPassword);

    [RelayCommand(CanExecute = nameof(CanSubmitLocalAuth))]
    private async Task LocalSignInAsync()
    {
        try
        {
            IsLocalBusy = true;
            LocalStatusText = "";

            var username = LocalUsername.Trim();
            var valid = await _localAccountStore.ValidateAsync(username, LocalPassword);
            if (!valid)
            {
                LocalStatusText = "Неверное имя пользователя или пароль.";
                return;
            }

            await ActivateLocalAccountAsync(username);
            LocalStatusText = $"Вход выполнен: {username}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local sign-in failed");
            LocalStatusText = $"Ошибка входа: {ex.Message}";
        }
        finally
        {
            IsLocalBusy = false;
        }
    }

    private bool CanRegisterLocalAccount =>
        !IsLocalBusy
        && !string.IsNullOrWhiteSpace(LocalUsername)
        && !string.IsNullOrWhiteSpace(LocalPassword)
        && LocalPassword == LocalConfirmPassword;

    [RelayCommand(CanExecute = nameof(CanRegisterLocalAccount))]
    private async Task LocalRegisterAsync()
    {
        try
        {
            IsLocalBusy = true;
            LocalStatusText = "";

            if (LocalPassword.Length < 4)
            {
                LocalStatusText = "Пароль должен быть не короче 4 символов.";
                return;
            }

            var username = LocalUsername.Trim();
            await _localAccountStore.RegisterAsync(username, LocalPassword);
            await ActivateLocalAccountAsync(username);
            LocalStatusText = $"Аккаунт создан, вход выполнен: {username}";
        }
        catch (InvalidOperationException ex)
        {
            LocalStatusText = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local registration failed");
            LocalStatusText = $"Ошибка регистрации: {ex.Message}";
        }
        finally
        {
            IsLocalBusy = false;
        }
    }

    /// <summary>Local accounts have no separate backend identity — signing in just makes this username
    /// the active offline profile, the same one the plain "Никнейм" field edits.</summary>
    private async Task ActivateLocalAccountAsync(string username)
    {
        var settings = await _settingsService.LoadAsync();
        settings.LocalAccountUsername = username;
        settings.PreferredNickname = username;
        settings.UseMicrosoftAccount = false;
        await _settingsService.SaveAsync(settings);

        IsLocalSignedIn = true;
        LocalSignedInUsername = username;
        Nickname = username;
        LocalUsername = "";
        LocalPassword = "";
        LocalConfirmPassword = "";

        // Fire-and-forget: friends should see us online right away, not after the next minute tick.
        _ = _presenceService.PokeAsync();
        _ = LoadSkinAsync();
        WeakReferenceMessenger.Default.Send(new AccountSummaryChangedMessage());
    }

    [RelayCommand]
    private async Task LocalSignOutAsync()
    {
        var settings = await _settingsService.LoadAsync();
        var username = settings.LocalAccountUsername;
        settings.LocalAccountUsername = null;
        await _settingsService.SaveAsync(settings);

        IsLocalSignedIn = false;
        LocalSignedInUsername = null;
        LocalStatusText = "";
        WeakReferenceMessenger.Default.Send(new AccountSummaryChangedMessage());

        if (!string.IsNullOrWhiteSpace(username))
        {
            try
            {
                await _friendsService.GoOfflineAsync(username);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Presence offline on sign-out failed");
            }
        }
    }

    [RelayCommand]
    private async Task LoadActiveProfileAsync()
    {
        var settings = await _settingsService.LoadAsync();
        _nickname = settings.PreferredNickname;
        OnPropertyChanged(nameof(Nickname));
    }

    partial void OnNicknameChanged(string value) => SaveActiveProfileCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task SaveActiveProfileAsync()
    {
        var settings = await _settingsService.LoadAsync();
        settings.PreferredNickname = string.IsNullOrWhiteSpace(Nickname) ? "Player" : Nickname;
        // Microsoft support was removed; make sure old configs never route launches through it.
        settings.UseMicrosoftAccount = false;
        await _settingsService.SaveAsync(settings);
        WeakReferenceMessenger.Default.Send(new AccountSummaryChangedMessage());
    }

}
