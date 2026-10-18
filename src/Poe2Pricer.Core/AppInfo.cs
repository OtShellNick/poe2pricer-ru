using System.Reflection;

namespace Poe2Pricer.Core;

public static class AppInfo
{
    // The program's version: <Version> in Directory.Build.props. The build may append the
    // commit after a "+"; that part is not shown.
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    // Name and version as shown to the user: "poe2pricer v0.1.0".
    public static string NameAndVersion => $"poe2pricer v{Version}";
}
