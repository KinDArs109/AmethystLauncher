namespace Launcher.App.ViewModels;

public enum GameLogLevel
{
    Info,
    Warn,
    Error,
}

public sealed record GameLogEntry(string Text, GameLogLevel Level)
{
    public static GameLogLevel ClassifyLevel(string text, bool isErrorStream)
    {
        // Minecraft's own log4j output tags the level in the line itself (e.g. "[Render thread/WARN]:").
        if (text.Contains("/FATAL]") || text.Contains("/ERROR]"))
        {
            return GameLogLevel.Error;
        }

        if (text.Contains("/WARN]"))
        {
            return GameLogLevel.Warn;
        }

        return isErrorStream ? GameLogLevel.Warn : GameLogLevel.Info;
    }
}
