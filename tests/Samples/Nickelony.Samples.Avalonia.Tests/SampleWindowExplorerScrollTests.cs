using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Nickelony.Samples.Avalonia.Tests;

/// <summary>
/// Covers how the explorer of the window keeps its scroll position and its selected row when it is rebuilt, which the Avalonia tree view
/// and its scroll viewer decide.
/// </summary>
[TestClass]
public sealed class SampleWindowExplorerScrollTests
{
	[TestMethod]
	public async Task Explorer_KeepsItsScrollPosition_WhenItIsRebuilt()
	{
		using var directory = new TemporaryDirectory("sample-");

		for (int index = 0; index < 80; index++)
			File.WriteAllText(Path.Combine(directory.Path, $"zfile{index:D2}.txt"), "x");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				window.Width = 800;
				window.Height = 500;
				window.UpdateLayout();

				TreeView explorer = window.GetLogicalDescendants().OfType<TreeView>().Single();
				ScrollViewer scroller = explorer.GetVisualDescendants().OfType<ScrollViewer>().First();

				Assert.IsGreaterThan(
					0.0,
					scroller.Extent.Height - scroller.Viewport.Height,
					"The explorer must be taller than its viewport for the premise to hold."
				);

				scroller.Offset = new Vector(0, 300);
				await Task.Delay(300);

				double before = scroller.Offset.Y;

				Assert.IsGreaterThan(0.0, before, "The explorer did not scroll.");

				// The explorer is rebuilt after a change of the folder, and the rebuild must leave the scroll position alone.
				window.Workbench.Explorer.Refresh();
				Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

				Assert.AreEqual(before, window.ExplorerScrollOffset.Y, 1.0);
			}
		);
	}

	[TestMethod]
	public async Task Explorer_KeepsTheRowOfAFileWhereItIsWhileItIsInView_AndScrollsItIntoViewWhenItIsNot()
	{
		using var directory = new TemporaryDirectory("sample-");

		for (int index = 0; index < 80; index++)
			File.WriteAllText(Path.Combine(directory.Path, $"zfile{index:D2}.txt"), "x");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				window.Width = 800;
				window.Height = 500;
				window.UpdateLayout();

				TreeView explorer = window.GetLogicalDescendants().OfType<TreeView>().Single();
				ScrollViewer scroller = explorer.GetVisualDescendants().OfType<ScrollViewer>().First();

				static bool InView(TreeViewItem row, ScrollViewer scroller) =>
					row.TranslatePoint(default, scroller) is { } top && top.Y >= -1.0 && top.Y + row.Bounds.Height <= scroller.Viewport.Height + 1.0;

				scroller.Offset = new Vector(0, 300);
				await Task.Delay(300);

				// A row in the middle of the view is the selected one, as it is when its file opens from a double-click.
				TreeViewItem[] visible = [.. explorer.GetVisualDescendants().OfType<TreeViewItem>().Where(row => InView(row, scroller))];
				TreeViewItem middle = visible[visible.Length / 2];

				explorer.SelectedItem = middle;
				await Task.Delay(300);

				double before = scroller.Offset.Y;
				double rowTop = middle.TranslatePoint(default, scroller)!.Value.Y;

				window.Workbench.Explorer.Refresh();
				Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

				Assert.AreEqual(expected: before, actual: scroller.Offset.Y, delta: 1.0, message: "The row of the selected file moved.");

				// A file whose row is in view opens without moving the list, as a double-click opens it.
				await window.Workbench.OpenAsync(((SampleExplorerNode)middle.Tag!).Path);
				Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
				await Task.Delay(300);

				Assert.AreEqual(expected: before, actual: scroller.Offset.Y, delta: 1.0, message: "The row of the opened file moved.");

				// A file below the view scrolls into view when it opens.
				string last = Path.Combine(directory.Path, "zfile79.txt");
				await window.Workbench.OpenAsync(last);
				Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
				await Task.Delay(300);

				TreeViewItem opened = explorer
					.GetVisualDescendants()
					.OfType<TreeViewItem>()
					.Single(row => row.Tag is SampleExplorerNode node && node.Path == last);

				Assert.IsTrue(InView(opened, scroller), "The row of the opened file is out of view.");
				Assert.IsGreaterThan(-1.0, rowTop);
			}
		);
	}
}
