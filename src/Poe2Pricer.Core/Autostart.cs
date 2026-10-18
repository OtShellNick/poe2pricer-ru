using Microsoft.Win32;

namespace Poe2Pricer.Core;

// "Start with Windows": an entry for this user under
// HKCU\Software\Microsoft\Windows\CurrentVersion\Run, no administrator rights needed. The
// setting says whether it should be there; the entry follows it — when the settings are saved
// and at every start, where an entry that names another place (the program was moved) is set
// right without asking.
// read/write: the entry's value, null = no entry; the tests pass their own.
public sealed class Autostart(Func<string?> read, Action<string?> write)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "poe2pricer";

    public static Autostart Windows() =>
        new(
            () =>
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(Name) as string;
            },
            value =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value is null)
                    key.DeleteValue(Name, throwOnMissingValue: false);
                else
                    key.SetValue(Name, value);
            }
        );

    // The entry as Windows wants it: the program's place in quotes.
    public static string Entry(string program) => $"\"{program}\"";

    // True: the entry had to be written or removed.
    public bool Apply(bool wanted, string program)
    {
        var should = wanted ? Entry(program) : null;
        if (read() == should)
            return false;
        write(should);
        return true;
    }
}
