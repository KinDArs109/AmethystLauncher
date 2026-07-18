namespace Launcher.Backend.Supabase;

/// <summary>
/// Public Supabase project identifiers for this launcher build. The anon key is meant to be shipped
/// in client apps — it has no privileges beyond what Row Level Security policies grant it.
/// </summary>
public static class SupabaseConfig
{
    public const string Url = "https://camxoptnyxrljaamsfym.supabase.co";
    public const string AnonKey = "sb_publishable_GeOO-3f-CDW-9JR9SWxlbQ_TOb4SrNr";
}
