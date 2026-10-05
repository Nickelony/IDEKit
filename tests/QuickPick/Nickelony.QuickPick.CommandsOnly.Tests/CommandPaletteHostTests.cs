using System.Windows.Controls;
using Nickelony.QuickPick.Wpf;

namespace Nickelony.QuickPick.CommandsOnly.Tests;

/// <summary>
/// Covers a host that wants a command palette and nothing else: this project references the quick pick and its WPF window only, registers one mode
/// under the empty prefix with the help mode off, opens the palette, filters it and runs the command that the user accepts.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class CommandPaletteHostTests
{
	private static readonly QuickPickControllerOptions s_options = QuickPickControllerOptions.Default with
	{
		EnableHelpMode = false,
		QueryDebounceDelay = TimeSpan.Zero,
	};

	[TestMethod]
	public void OpenAndAccept_OneModeWithTheHelpModeOff_RunsTheCommandOfTheActiveRow()
	{
		var commands = new RecordingCommandProvider("Format Document", "Save File", "Close All");
		var surface = new Border { Height = 300.0 };

		using HostWindow window = WpfTestHost.ShowInHostWindow(surface);

		TestWindowActivation.Activate(window);

		WpfTestHost.EnsureUiContext();

		using var controller = new QuickPickController([new QuickPickMode(commands, prefix: string.Empty)], options: s_options);
		using var presenter = new QuickPickPresenter(surface, controller);

		controller.Open();
		DispatcherTestHelpers.PumpUntil(() => controller.State.Rows.Count == 3);

		Assert.IsTrue(presenter.IsOpen);

		controller.SetText("fmt");
		DispatcherTestHelpers.PumpUntil(() => controller.State.Rows.Count == 1);

		Assert.AreEqual("Format Document", controller.State.ActiveItem?.Label);

		Task<bool> accept = controller.AcceptAsync();

		DispatcherTestHelpers.PumpUntil(() => accept.IsCompleted);

		Assert.IsTrue(accept.Result);
		Assert.AreSequenceEqual(["Format Document"], [.. commands.Executed]);
		Assert.IsFalse(presenter.IsOpen);
	}

	[TestMethod]
	public void SetText_TheHelpAndCommandCharacters_FilterAsPlainText()
	{
		var commands = new RecordingCommandProvider("Save File", "What is this?", "> prompt");
		var surface = new Border { Height = 300.0 };

		using HostWindow window = WpfTestHost.ShowInHostWindow(surface);

		TestWindowActivation.Activate(window);

		WpfTestHost.EnsureUiContext();

		using var controller = new QuickPickController([new QuickPickMode(commands, prefix: string.Empty)], options: s_options);
		using var presenter = new QuickPickPresenter(surface, controller);

		controller.Open("?");
		DispatcherTestHelpers.PumpUntil(() => controller.State.Rows.Count == 1);

		Assert.AreEqual("Type a command", controller.State.Placeholder);
		Assert.AreEqual("What is this?", controller.State.ActiveItem?.Label);

		controller.SetText(">");
		DispatcherTestHelpers.PumpUntil(() => controller.State.ActiveItem?.Label == "> prompt");

		Assert.AreEqual("Type a command", controller.State.Placeholder);
	}
}
