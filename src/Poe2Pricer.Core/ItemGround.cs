using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Recognition;

namespace Poe2Pricer.Core;

// The ground the game draws behind an item, in every grid alike (Favours, stash): dark blue,
// red when the character cannot use the item, green under the mouse (keyboard UI). One rule
// for all windows: a blue-only rule loses the price under the mouse.
public static class ItemGround
{
    public static bool Blue(Vec3b p) => p.Item0 > p.Item2 * Tune.BlueOverRed + Tune.ColourLead && p.Item0 > p.Item1 * Tune.BlueOverGreen + Tune.BlueOverGreenLead;

    public static bool Red(Vec3b p) => p.Item2 > p.Item0 * Tune.RedOverOthers + Tune.ColourLead && p.Item2 > p.Item1 * Tune.RedOverOthers + Tune.ColourLead;

    public static bool Green(Vec3b p) => p.Item1 > p.Item0 * Tune.GreenOverOthers + Tune.ColourLead && p.Item1 > p.Item2 * Tune.GreenOverOthers + Tune.ColourLead;

    public static bool Any(Vec3b p) => Blue(p) || Red(p) || Green(p);
}
