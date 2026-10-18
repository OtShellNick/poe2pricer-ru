using System.IO;
using Poe2Pricer.Core;

namespace Poe2Pricer.App;

// %LOCALAPPDATA%\poe2pricer\log.txt: what the overlay saw and did, one line per event with the
// time. Written from the overlay control and image work threads.
public static class FileLog
{
    private static readonly string File_ = Path.Combine(Settings.Folder, "log.txt");
    private static readonly Lock Gate = new();

    public static string Place => File_;

    // Once per run, before the first line: a log grown too large is put aside (LogFile).
    private static bool begun;

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Folder);
                if (!begun)
                {
                    begun = true;
                    LogFile.PutAside(File_);
                }
                File.AppendAllText(File_, $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
    }
}
