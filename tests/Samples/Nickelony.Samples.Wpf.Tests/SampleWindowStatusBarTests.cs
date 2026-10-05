using System.Windows.Controls;

namespace Nickelony.Samples.Wpf.Tests;

/// <summary>
/// Covers the status bar menus of the window, which draw their check marks through the checkable state that the Fluent template reads.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class SampleWindowStatusBarTests
{
	[TestMethod]
	public async Task StatusBarMenus_DrawACheckMarkForTheActiveEntries()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				window.Workbench.ActivePane = window.Workbench.LuaPane;
				IReadOnlyList<MenuItem> items = window.CheckableStatusItems;

				Assert.IsNotEmpty(items);
				Assert.IsTrue(items.All(item => item.IsCheckable), "The Fluent template draws the mark of a checkable item only.");

				// The sample script is four spaces, UTF-8 and one line terminator style, so the matching entries of each list carry the mark.
				Assert.IsGreaterThanOrEqualTo(4, items.Count(item => item.IsChecked));

				return Task.CompletedTask;
			}
		);
	}
}
