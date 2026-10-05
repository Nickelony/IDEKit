using System.IO;
using Nickelony.IDEKit.Core.Indentation;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the indentation of the sample tabs: what a file shows, what the status bar says and what the commands convert.</summary>
[STATestClass]
public sealed class SampleIndentationTests
{
	private const string TabIndented = "function f()\n\tif x then\n\t\ty()\n\tend\nend\n";

	private static void Execute(SampleWorkbench workbench, string commandId) =>
		workbench.Commands.First(command => command.Id == commandId).Execute();

	[TestMethod]
	public async Task OpeningTabs_ApplyTheIndentationEachFileShows()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.ActivePane = workbench.LuaPane;
				Assert.AreEqual("Spaces: 4", workbench.IndentationDescription);

				// The settings file indents with two spaces.
				workbench.ActivePane = workbench.SettingsPane;
				Assert.AreEqual("Spaces: 2", workbench.IndentationDescription);

				string tabbedPath = Path.Combine(directory.Path, "tabbed.lua");
				await File.WriteAllTextAsync(tabbedPath, TabIndented);
				SamplePane tabbed = await workbench.OpenAsync(tabbedPath);

				Assert.AreEqual("Tab Size: 4", workbench.IndentationDescription);
				Assert.IsFalse(tabbed.Editor.Options.ConvertTabsToSpaces);
			}
		);
	}

	[TestMethod]
	public async Task OpeningATabThatShowsNoIndentation_TakesTheTabSizeOfTheSettings()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				string plainPath = Path.Combine(directory.Path, "plain.lua");
				await File.WriteAllTextAsync(plainPath, "print(1)\n");
				SamplePane plain = await workbench.OpenAsync(plainPath);

				Assert.IsTrue(plain.Editor.Options.ConvertTabsToSpaces);
				Assert.AreEqual(SampleSettings.Default.TabSize, plain.Editor.Options.IndentationSize);
			}
		);
	}

	[TestMethod]
	public async Task SavingTheSettings_GivesTheNewTabSizeToTheTabsThatShowNoIndentation()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane lua = workbench.LuaPane;
				SamplePane settings = workbench.SettingsPane;

				lua.Editor.Text = "print(1)";
				settings.Editor.Text = settings.Editor.Text.Replace("\"tabSize\": 4", "\"tabSize\": 3", StringComparison.Ordinal);

				await workbench.SaveAsync(settings);

				Assert.AreEqual(3, lua.Editor.Options.IndentationSize);
				Assert.AreEqual(2, settings.Editor.Options.IndentationSize);
			}
		);
	}

	[TestMethod]
	public async Task UseIndentation_ChangesWhatTheTabKeyInsertsAndLeavesTheText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				SamplePane lua = workbench.LuaPane;
				workbench.ActivePane = lua;

				string text = lua.Editor.Text;
				int raised = 0;

				workbench.IndentationChanged += (_, _) => raised++;

				workbench.UseIndentation(insertSpaces: false, size: 8);

				Assert.AreEqual("Tab Size: 8", workbench.IndentationDescription);
				Assert.IsTrue(workbench.UsesIndentation(insertSpaces: false, size: 8));
				Assert.IsFalse(workbench.UsesIndentation(insertSpaces: true, size: 8));
				Assert.AreEqual(text, lua.Editor.Text);
				Assert.AreEqual(1, raised);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task ConvertIndentationToTabs_ConvertsTheActivePaneAsOneUndoStep_AndKeepsTheOtherPane()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				SamplePane lua = workbench.LuaPane;
				SamplePane settings = workbench.SettingsPane;

				string luaText = lua.Editor.Text;
				string settingsText = settings.Editor.Text;

				workbench.ActivePane = lua;
				lua.Editor.Document.UndoStack.ClearAll();

				Execute(workbench, SampleCommandIds.ConvertIndentationToTabs);

				Assert.AreEqual(WhitespaceOperations.ConvertIndentationToTabs(luaText, 4), lua.Editor.Text);
				Assert.AreEqual(settingsText, settings.Editor.Text);
				Assert.AreEqual("Tab Size: 4", workbench.IndentationDescription);

				lua.Editor.Document.UndoStack.Undo();

				Assert.AreEqual(luaText, lua.Editor.Text);
				Assert.IsFalse(lua.Editor.Document.UndoStack.CanUndo);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task ConvertIndentationToSpaces_KeepsATabAfterTheFirstCharacter()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				SamplePane lua = workbench.LuaPane;
				workbench.ActivePane = lua;
				lua.Editor.Text = "\tprint(\"a\tb\")\t-- note";

				Execute(workbench, SampleCommandIds.ConvertIndentationToSpaces);

				Assert.AreEqual("    print(\"a\tb\")\t-- note", lua.Editor.Text);
				Assert.AreEqual("Spaces: 4", workbench.IndentationDescription);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task ConversionThatChangesNothing_ReportsItAndMakesNoUndoStep()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				SamplePane lua = workbench.LuaPane;
				workbench.ActivePane = lua;
				lua.Editor.Document.UndoStack.ClearAll();
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				Execute(workbench, SampleCommandIds.ConvertIndentationToSpaces);

				Assert.Contains("No line is indented with a tab; the editor now inserts spaces", messages);
				Assert.IsFalse(lua.Editor.Document.UndoStack.CanUndo);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task ReindentSpaces_ResizesTheLevelsOfTheTextToTheIndentSize()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				SamplePane lua = workbench.LuaPane;
				workbench.ActivePane = lua;
				lua.Editor.Text = "function f()\n    if x then\n        y()\n    end\nend\n";

				workbench.UseIndentation(insertSpaces: true, size: 2);
				Execute(workbench, SampleCommandIds.ReindentSpaces);

				Assert.AreEqual("function f()\n  if x then\n    y()\n  end\nend\n", lua.Editor.Text);
				Assert.AreEqual("Spaces: 2", workbench.IndentationDescription);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task ReindentSpaces_OfTextAlreadyAtTheIndentSize_ReportsItAndChangesNothing()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				SamplePane lua = workbench.LuaPane;
				workbench.ActivePane = lua;
				string text = lua.Editor.Text;
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				Execute(workbench, SampleCommandIds.ReindentSpaces);

				Assert.AreEqual(text, lua.Editor.Text);
				Assert.Contains("The text shows no space indentation of another size", messages);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task TheToolTip_SaysWhetherTheStyleAndTheSizeWereDetectedOrAreTheDefaults()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.ActivePane = workbench.LuaPane;
				Assert.AreEqual("Detected from the file: the style and the size", workbench.IndentationToolTip);

				string tabbedPath = Path.Combine(directory.Path, "tabbed.lua");
				await File.WriteAllTextAsync(tabbedPath, TabIndented);
				await workbench.OpenAsync(tabbedPath);
				Assert.AreEqual("Detected from the file: the style; the size is the tab size of the settings", workbench.IndentationToolTip);

				string plainPath = Path.Combine(directory.Path, "plain.lua");
				await File.WriteAllTextAsync(plainPath, "print(1)\n");
				await workbench.OpenAsync(plainPath);

				Assert.AreEqual(
					"The file shows no indentation, so the defaults apply: spaces, with the tab size of the settings",
					workbench.IndentationToolTip
				);

				workbench.UseIndentation(insertSpaces: false, size: 2);
				Assert.AreEqual("Chosen for this tab", workbench.IndentationToolTip);
			}
		);
	}

	[TestMethod]
	public async Task ConvertingInOneViewOfASplit_GivesTheOtherViewTheSameOptionsAndKeepsItsCaretOnItsText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane first = workbench.LuaPane;
				SamplePane? split = await workbench.SplitAsync(first);

				Assert.IsNotNull(split);

				int word = split.Editor.Text.IndexOf("greet(\"world\")", StringComparison.Ordinal) + 3;
				split.Editor.CaretOffset = word;
				workbench.ActivePane = first;

				Execute(workbench, SampleCommandIds.ConvertIndentationToTabs);

				await SampleTestHost.WaitUntilAsync(() => split.Editor.Text == first.Editor.Text, "The split did not follow the conversion.");

				Assert.IsFalse(split.Editor.Options.ConvertTabsToSpaces);
				workbench.ActivePane = split;

				Assert.AreEqual("Tab Size: 4", workbench.IndentationDescription);
				Assert.AreEqual("Chosen for this tab", workbench.IndentationToolTip);
				Assert.AreEqual("gre", split.Editor.Text.Substring(split.Editor.CaretOffset - 3, 3));
			}
		);
	}

	[TestMethod]
	public async Task IndentationCommands_AreTheThreeConversionCommandsOfTheEditMenu()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				string[] ids = [.. workbench.IndentationCommands.Select(command => command.Id)];

				Assert.AreSequenceEqual(
					[SampleCommandIds.ConvertIndentationToSpaces, SampleCommandIds.ConvertIndentationToTabs, SampleCommandIds.ReindentSpaces],
					ids
				);

				Assert.IsTrue(workbench.IndentationCommands.All(command => command.Menu == "_Edit"));

				return Task.CompletedTask;
			}
		);
	}
}
