using System.ComponentModel;
using Microsoft.Win32;

namespace Poe2Pricer.Core;

// Installing a feature of Windows on demand (the English text recognition) with Windows' own
// tool for it, dism.exe, started with administrator rights. Not by a script in PowerShell: a
// hidden PowerShell with a command line of that kind is what malware looks like, and virus
// scanners block it. What went wrong comes back as the exit code — Windows' own error number.
public static class CapabilityInstall
{
    // With its full path: started by its bare name, Windows would look in the folder the
    // program was started from first, and a foreign file of that name there would run with
    // administrator rights.
    public static string Tool => Path.Combine(Environment.SystemDirectory, "dism.exe");

    public static string Arguments(string capability) => $"/Online /Add-Capability /CapabilityName:{capability} /NoRestart /Quiet";

    // Windows has only pended the installation: nothing sees the feature before a restart of
    // Windows, so a start of the program would offer the installation again.
    public const int RestartRequired = 3010;

    // null: installed. Otherwise the error as Windows numbers it: a small number with its text
    // (740: needs administrator rights), a large one as it is written and searched for
    // (0x800F0954). RestartRequired is answered before this is asked.
    public static string? Outcome(int exitCode) =>
        exitCode switch
        {
            0 => null,
            > 0 and < 0x10000 => $"DISM error {exitCode}: {new Win32Exception(exitCode).Message}",
            _ => $"DISM error 0x{exitCode:X8}",
        };

    // Windows has servicing work (an update, a feature) waiting for its restart: the key is
    // there until then, readable without administrator rights. In that state an installation
    // is only pended behind that work (RestartRequired after minutes of waiting), and a restart
    // from the Start menu may put the work off once more; the one from Settings → Windows
    // Update does it.
    public const string RestartPendingKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";

    public static bool RestartPending()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RestartPendingKey);
        return key is not null;
    }

    public enum Result
    {
        // Seen by this run.
        Installed,
        // Outcome has the error.
        Failed,
        // Pended by Windows; nothing sees the feature before Windows restarts.
        RestartWindows,
        // Installed, but this run does not see it.
        RestartProgram,
    }

    // What to tell after dism ended: available — whether the feature is seen now;
    // restartPending — whether Windows has a restart due (RestartPending).
    public static Result After(int exitCode, bool available, bool restartPending) =>
        exitCode switch
        {
            0 or RestartRequired when available => Result.Installed,
            RestartRequired => Result.RestartWindows,
            0 => restartPending ? Result.RestartWindows : Result.RestartProgram,
            _ => Result.Failed,
        };
}
