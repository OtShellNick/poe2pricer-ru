namespace Poe2Pricer.Core.Market;

// What waystone and tablet tooltip readings do alike with their mod lines.
public static class ModRows
{
    // The mod lines of a reading with what a later reading adds: a line the later one found
    // and this one did not, a value the OCR lost here and found there. Lines are told apart
    // by key (the mod's text).
    public static List<T> Merged<T>(IEnumerable<T> mine, IEnumerable<T> later, Func<T, string> key, Func<T, bool> hasValue)
    {
        var mods = mine.ToList();
        foreach (var mod in later)
        {
            int i = mods.FindIndex(m => key(m) == key(mod));
            if (i < 0)
                mods.Add(mod);
            else if (!hasValue(mods[i]) && hasValue(mod))
                mods[i] = mod;
        }
        return mods;
    }
}
