namespace Launcher.App.Messages;

/// <summary>Broadcast whenever the active profile changes (nickname edited, launcher-account
/// sign-in/out, Microsoft toggle) so always-visible UI like the top-right "Играете как" card
/// refreshes immediately instead of waiting for an app restart.</summary>
public sealed class AccountSummaryChangedMessage;
