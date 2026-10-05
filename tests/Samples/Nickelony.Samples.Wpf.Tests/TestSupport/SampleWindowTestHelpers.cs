using System.Windows;
using System.Windows.Controls;

namespace Nickelony.Samples.Wpf.Tests;

/// <summary>Reads the parts of the window that the toolkit decides: the rows of the explorer, the groups of tabs and the tool tips.</summary>
internal static class SampleWindowTestHelpers
{
	/// <summary>Reads the path of the file that the selected explorer row stands for, or <see langword="null"/> when no row is selected.</summary>
	internal static string? SelectedExplorerPath(SampleWindow window) =>
		ExplorerOf(window).SelectedItem is TreeViewItem { Tag: SampleExplorerNode node } ? node.Path : null;

	/// <summary>Reports whether the folder row above the selected explorer row is expanded.</summary>
	internal static bool SelectedExplorerRowIsInAnExpandedFolder(SampleWindow window) =>
		ExplorerOf(window).SelectedItem as TreeViewItem is { } row && LogicalTreeHelper.GetParent(row) is TreeViewItem { IsExpanded: true };

	/// <summary>Finds the group of tabs that shows the editor of the pane.</summary>
	internal static object? TabGroupOf(SamplePane pane)
	{
		for (DependencyObject? parent = LogicalTreeHelper.GetParent(pane.Editor); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
		{
			if (parent is TabControl tabs)
				return tabs;
		}

		return null;
	}

	/// <summary>Reads the tool tip of the status line.</summary>
	internal static object? StatusLineToolTip(SampleWindow window) => window.StatusLine.ToolTip;

	private static TreeView ExplorerOf(SampleWindow window) => Descendants<TreeView>(window).Single();

	private static IEnumerable<T> Descendants<T>(DependencyObject root)
		where T : DependencyObject
	{
		foreach (DependencyObject child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
		{
			if (child is T match)
				yield return match;

			foreach (T nested in Descendants<T>(child))
				yield return nested;
		}
	}
}
