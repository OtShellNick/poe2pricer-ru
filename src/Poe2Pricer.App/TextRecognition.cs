using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Poe2Pricer.Core;

namespace Poe2Pricer.App;

// The English text recognition of Windows, without which the program reads no tooltip and no
// rumour (TitleReader). It is a feature Windows installs on demand, not something the program
// can carry along; on a Windows set up in another language it may be missing. An installer
// offers it while installing; this is for whoever unpacked the program instead, and finds
// nothing to do after an installation. No other language is taken in its
// place: measured with the German one, the raw text differs, and nothing but German was measured.
public static class TextRecognition
{
    public const string Language = "en-US";

    // The name Windows knows the feature by (DISM).
    public const string Capability = "Language.OCR~~~en-US~0.0.1.0";

    // True: there, or installed just now. False: missing and not installed — the program ends.
    // POE2PRICER_NO_TEXT_RECOGNITION: taken for missing (to see the offer on a Windows that has it).
    public static bool Ensure()
    {
        bool pretend = Environment.GetEnvironmentVariable("POE2PRICER_NO_TEXT_RECOGNITION") is { Length: > 0 };
        if (TitleReader.Available(Language) && !pretend)
            return true;
        // With a restart of Windows due, dism would wait minutes only to pend the feature behind
        // the waiting work: the restart first, then the offer if it is still missing.
        if (CapabilityInstall.RestartPending())
        {
            FileLog.Write($"Text recognition {Language} missing; Windows has a restart due, not offered");
            MessageBox.Show(Texts.Get("TextRecognitionRestartPending"), "poe2pricer", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        FileLog.Write($"Text recognition {Language} missing; offered to install it");
        if (MessageBox.Show(Texts.Get("TextRecognitionOffer"), "poe2pricer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;
        string? notStarted = Install(out bool cancelled, out int exitCode);
        // Given up waiting: the installation may go on or hang, either way without us.
        if (cancelled)
        {
            FileLog.Write($"Text recognition {Language}: waiting for the installation cancelled");
            MessageBox.Show(Texts.Get("TextRecognitionCancelled"), "poe2pricer", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var result = notStarted is not null ? CapabilityInstall.Result.Failed : CapabilityInstall.After(exitCode, TitleReader.Available(Language), CapabilityInstall.RestartPending());
        switch (result)
        {
            case CapabilityInstall.Result.Installed:
                FileLog.Write($"Text recognition {Language} installed");
                return true;
            case CapabilityInstall.Result.RestartWindows:
                FileLog.Write($"Text recognition {Language} installed; Windows wants a restart for it");
                MessageBox.Show(Texts.Get("TextRecognitionWindowsRestart"), "poe2pricer", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            case CapabilityInstall.Result.RestartProgram:
                FileLog.Write($"Text recognition {Language} not installed: installed, but not available to this run");
                MessageBox.Show(Texts.Get("TextRecognitionRestart"), "poe2pricer", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            default:
                string failure = notStarted ?? CapabilityInstall.Outcome(exitCode)!;
                FileLog.Write($"Text recognition {Language} not installed: {failure}");
                MessageBox.Show(Texts.Get("TextRecognitionFailed", failure), "poe2pricer", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
        }
    }

    // Windows' own installation, with administrator rights (Windows asks). null: dism ran and
    // ended with exitCode (CapabilityInstall.After). Otherwise why it could not be started.
    // While it runs — minutes, if the feature has to be fetched — a window says so.
    // POE2PRICER_TEXT_RECOGNITION_FAIL: asks for a feature Windows does not have (to see how
    // a failure is told on a Windows where the installation would succeed).
    private static string? Install(out bool cancelled, out int exitCode)
    {
        cancelled = false;
        exitCode = 0;
        bool fail = Environment.GetEnvironmentVariable("POE2PRICER_TEXT_RECOGNITION_FAIL") is { Length: > 0 };
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo(CapabilityInstall.Tool, CapabilityInstall.Arguments(fail ? "Language.OCR~~~xx-XX~0.0.1.0" : Capability))
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                }
            );
            if (process is null)
                return "dism.exe";
            cancelled = !Poe2Pricer.Ui.Views.WaitWindow.While(Texts.Get("TextRecognitionInstalling"), Texts.Get("Cancel"), process.WaitForExitAsync(), Texts.Get("TextRecognitionInstallingFor"));
            if (cancelled)
                return null;
            exitCode = process.ExitCode;
            FileLog.Write($"Text recognition {Language}: dism exit code {exitCode}");
            return null;
        }
        // 1223: the request for administrator rights was declined.
        catch (Win32Exception e)
        {
            return e.Message;
        }
    }
}
