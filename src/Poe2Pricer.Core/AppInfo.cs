using System.Reflection;

namespace Poe2Pricer.Core;

public static class AppInfo
{
    // What changed in each version, on github.com; the version number in the window header
    // links to it (opens in the browser on a click, the program itself requests nothing).
    public const string ChangelogPage = "https://github.com/saxe78/poe2pricer/blob/main/CHANGELOG.md";

    // The program's version: <Version> in Directory.Build.props. The build may append the
    // commit after a "+"; that part is not shown.
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    // Name and version as shown to the user: "poe2pricer v0.1.0".
    public static string NameAndVersion => $"poe2pricer v{Version}";
}
