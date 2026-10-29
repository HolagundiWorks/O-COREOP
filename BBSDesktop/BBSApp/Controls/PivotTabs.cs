// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BBSApp.Controls;

/// <summary>
/// Hides a pivot's own tab strip when the sub ribbon already lists the same pages, so each page is named once.
/// The pivot keeps working: the page still picks its tab with <see cref="Pivot.SelectedIndex"/>.
/// </summary>
public static class PivotTabs
{
    public static void HideHeaders(Pivot pivot)
    {
        pivot.Loaded += (_, _) => Collapse(pivot);
        pivot.LayoutUpdated += (_, _) => Collapse(pivot);
    }

    private static void Collapse(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is PivotHeaderPanel panel)
            {
                // The header strip is the panel's parent chain up to the pivot's header clipper; collapse the panel's row.
                if (panel.Visibility != Visibility.Collapsed) panel.Visibility = Visibility.Collapsed;
                continue;
            }
            if (child is FrameworkElement fe && fe.Name is "PreviousButton" or "NextButton" or "HeaderClipper" or "StaticHeader" or "Header"
                && fe.Visibility != Visibility.Collapsed)
            {
                fe.Visibility = Visibility.Collapsed;
                continue;
            }
            Collapse(child);
        }
    }
}
