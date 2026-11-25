using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JamesOptimizer.WinUI.Core;

internal static class SegmentedPillHelper
{
    private static (Style Active, Style Normal) GetPillStyles() =>
        ((Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"],
         (Style)Application.Current.Resources["SegmentedPillButtonStyle"]);

    public static void SelectPill(string activeTag, params (Button Button, string Tag)[] items)
    {
        var (active, normal) = GetPillStyles();
        foreach (var (button, tag) in items)
            button.Style = string.Equals(tag, activeTag, StringComparison.Ordinal) ? active : normal;
    }

    public static void SelectTab(string activeTag, params (Button Button, FrameworkElement Panel, string Tag)[] tabs)
    {
        var (active, normal) = GetPillStyles();
        foreach (var (button, panel, tag) in tabs)
        {
            bool isSelected = string.Equals(tag, activeTag, StringComparison.Ordinal);
            button.Style = isSelected ? active : normal;
            panel.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
