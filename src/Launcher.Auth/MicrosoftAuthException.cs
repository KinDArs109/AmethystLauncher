namespace Launcher.Auth;

/// <summary>Any failure along the MSA → XBL → XSTS → Minecraft Services chain (see Microsoft/, Xbox/, MinecraftServices/).</summary>
public sealed class MicrosoftAuthException(string message, Exception? inner = null) : Exception(message, inner);
