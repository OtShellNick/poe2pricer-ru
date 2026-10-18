namespace Poe2Pricer.Core;

// The log grows with every look (several MB a week). At the start of the program a log over
// the limit is put aside as the one before — one is kept, the one before that is gone — and a
// new one begins.
public static class LogFile
{
    public const long Limit = 2 * 1024 * 1024;

    public static string Previous(string file) =>
        Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file) + "-previous" + Path.GetExtension(file));

    // True: put aside. A log that cannot be moved (held by another run) stays and grows on.
    public static bool PutAside(string file, long limit = Limit)
    {
        try
        {
            var log = new FileInfo(file);
            if (!log.Exists || log.Length <= limit)
                return false;
            File.Move(file, Previous(file), overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
