namespace Launcher.Core.Launch;

public static class ClasspathBuilder
{
    public static string Build(IEnumerable<string> jarPaths) => string.Join(';', jarPaths);
}
