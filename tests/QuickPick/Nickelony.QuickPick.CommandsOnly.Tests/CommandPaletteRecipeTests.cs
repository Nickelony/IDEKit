using Nickelony.KeyBindings;

namespace Nickelony.QuickPick.CommandsOnly.Tests;

/// <summary>
/// Pins the command palette recipe of the quick pick guide: a host that references the key bindings and the quick pick only builds
/// the items of the command mode from a <see cref="CommandCatalog{TCommandId}"/>, filters them and runs the command that the user accepts.
/// The project has no reference to an IDEKit package, so the recipe compiles without one.
/// </summary>
[TestClass]
public sealed class CommandPaletteRecipeTests
{
	private enum PaletteCommand
	{
		None,
		FormatDocument,
		SaveFile,
		CloseAll,
	}

	private static CommandCatalog<PaletteCommand> CreateCatalog(KeyBindingPlatformProfile platform)
	{
		return new([
			new CommandDescriptor<PaletteCommand>(
				PaletteCommand.FormatDocument,
				"FormatDocument",
				CommandRemappingPolicy.Remappable,
				platform.PrimaryStroke(KeyCode.F, KeyModifiers.Shift)
			)
			{
				Title = "Format Document",
				Category = "Edit",
			},

			new CommandDescriptor<PaletteCommand>(
				PaletteCommand.SaveFile,
				"SaveFile",
				CommandRemappingPolicy.Remappable,
				platform.PrimaryStroke(KeyCode.S)
			)
			{
				Title = "Save File",
				Category = "File",
			},

			new CommandDescriptor<PaletteCommand>(PaletteCommand.CloseAll, "CloseAll", CommandRemappingPolicy.Remappable) { Category = "File" },
		]);
	}

	[TestMethod]
	public void Open_TheRecipeOverACommandCatalog_ListsEveryCommandWithItsTitleItsCategoryAndItsShortcut()
	{
		KeyBindingPlatformProfile platform = KeyBindingPlatformProfile.Windows;
		CommandCatalog<PaletteCommand> catalog = CreateCatalog(platform);

		var service = new KeyBindingService<PaletteCommand>(
			catalog,
			new FakeKeyBindingOverrideStore(),
			new KeyBindingServiceOptions { DisplayTextFormatter = new DesktopKeyDisplayTextFormatter(platform) }
		);

		var provider = new FakeCommandQuickPickProvider<PaletteCommand>(catalog, service, canRunNow: _ => true, run: _ => { });

		using var host = new QuickPickTestHost(provider);

		host.Open(">");

		// A command without a title shows its serialized id, and the items are ordered by that label.
		Assert.AreSequenceEqual(["CloseAll", "Format Document", "Save File"], [.. host.Labels]);

		QuickPickItem[] items = [.. host.Controller.State.Rows.Where(row => row.Item is not null).Select(row => row.Item!)];

		Assert.AreSequenceEqual(["File", "Edit", "File"], [.. items.Select(item => item.Description)]);
		Assert.AreSequenceEqual([string.Empty, "Ctrl+Shift+F", "Ctrl+S"], [.. items.Select(item => item.TrailingText)]);

		Assert.AreSequenceEqual(
			[PaletteCommand.CloseAll, PaletteCommand.FormatDocument, PaletteCommand.SaveFile],
			[.. items.Select(item => (PaletteCommand)item.Tag!)]
		);
	}

	[TestMethod]
	public void Accept_TheRecipeOverACommandCatalog_RunsTheCommandOfTheActiveRow()
	{
		KeyBindingPlatformProfile platform = KeyBindingPlatformProfile.Windows;
		CommandCatalog<PaletteCommand> catalog = CreateCatalog(platform);

		var service = new KeyBindingService<PaletteCommand>(catalog, new FakeKeyBindingOverrideStore());
		List<PaletteCommand> executed = [];

		var provider = new FakeCommandQuickPickProvider<PaletteCommand>(catalog, service, canRunNow: _ => true, run: executed.Add);

		using var host = new QuickPickTestHost(provider);

		host.Open(">");
		host.Type(">save");

		Assert.AreSequenceEqual(["Save File"], [.. host.Labels]);
		Assert.IsTrue(host.Accept());
		Assert.AreSequenceEqual([PaletteCommand.SaveFile], [.. executed]);
		Assert.IsFalse(host.Controller.State.IsOpen);
	}

	[TestMethod]
	public void Open_ACommandThatDoesNotApplyNow_IsListedDisabledAndNeverRuns()
	{
		KeyBindingPlatformProfile platform = KeyBindingPlatformProfile.Windows;
		CommandCatalog<PaletteCommand> catalog = CreateCatalog(platform);

		var service = new KeyBindingService<PaletteCommand>(catalog, new FakeKeyBindingOverrideStore());
		List<PaletteCommand> executed = [];

		var provider = new FakeCommandQuickPickProvider<PaletteCommand>(
			catalog,
			service,
			canRunNow: command => command != PaletteCommand.SaveFile,
			run: executed.Add
		);

		using var host = new QuickPickTestHost(provider);

		host.Open(">");
		host.Type(">save");

		Assert.AreSequenceEqual(["Save File"], [.. host.Labels]);
		Assert.IsFalse(host.Controller.State.Rows.Single(row => row.Item is not null).Item!.IsEnabled);
		Assert.IsFalse(host.Accept());
		Assert.IsEmpty(executed);
	}
}
