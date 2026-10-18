using System.Windows;
using System.Windows.Controls;

namespace Poe2Pricer.Ui.Views;

// Holds what must not make the window wider: asks for no width of its own and takes the width
// it is given, so a long line wraps instead of stretching the window sized to its content (the
// warnings above the foot, which would otherwise widen the window by a quarter). The height
// is that of the content at the width at hand.
public sealed class NoWider : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child)
            return default;
        child.Measure(constraint);
        return new Size(0, child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        Child?.Arrange(new Rect(size));
        return size;
    }
}
