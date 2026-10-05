using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Nickelony.Samples.Avalonia.Tests;

/// <summary>Reads the parts of the window that the toolkit decides: the rows of the explorer, the groups of tabs and the tool tips.</summary>
internal static class SampleWindowTestHelpers
{
	/// <summary>Reads the path of the file that the selected explorer row stands for, or <see langword="null"/> when no row is selected.</summary>
	internal static string? SelectedExplorerPath(SampleWindow window) =>
		ExplorerOf(window).SelectedItem is TreeViewItem { Tag: SampleExplorerNode node } ? node.Path : null;

	/// <summary>Reports whether the folder row above the selected explorer row is expanded.</summary>
	internal static bool SelectedExplorerRowIsInAnExpandedFolder(SampleWindow window) =>
		(ExplorerOf(window).SelectedItem as TreeViewItem)?.FindLogicalAncestorOfType<TreeViewItem>()?.IsExpanded == true;

	/// <summary>Finds the group of tabs that shows the editor of the pane.</summary>
	internal static object? TabGroupOf(SamplePane pane) => pane.Editor.FindLogicalAncestorOfType<TabControl>();

	/// <summary>Reads the tool tip of the status line.</summary>
	internal static object? StatusLineToolTip(SampleWindow window) => ToolTip.GetTip(window.StatusLine);

	private static TreeView ExplorerOf(SampleWindow window) => window.GetLogicalDescendants().OfType<TreeView>().Single();
}
