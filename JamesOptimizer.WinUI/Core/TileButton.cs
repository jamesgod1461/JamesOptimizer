using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace JamesOptimizer.WinUI.Core;

/// <summary>
/// Interactive Button with a hand cursor and template part lookup.
/// Elastic motion and dual shadows are driven by ElasticBehavior via style.
/// </summary>
public sealed class TileButton : Button
{
    public TileButton()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    public Border? HoverGlowBorder => GetTemplateChild("PART_AccentRim") as Border ?? GetTemplateChild("PART_HoverGlow") as Border;
}
