namespace Poe2Pricer.Core.Market;

// The tolerance of the Waystones and Tablets tabs, in per cent of a value: how far below a
// value the trade site is asked (an item's own 36 with 10 % asks for 33 or more), and how far
// below a profile's minimum an item still counts as meeting it. In per cent, so that a line
// rolling 1–2 keeps its number while 30–40 gets room (points would wipe out the one and barely
// touch the other).
public static class Tolerance
{
    public const int Default = 10;

    // The value lowered by the percentage, rounded up: never below 1 for a positive value.
    public static int Lower(int value, int percent) => percent <= 0 || value <= 0 ? value : (int)Math.Ceiling(value * (100 - percent) / 100.0);

    public static bool Reaches(int value, int minimum, int percent) => value >= Lower(minimum, percent);
}
