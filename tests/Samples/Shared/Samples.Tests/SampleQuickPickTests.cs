using System.Diagnostics;
using System.IO;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;
using Nickelony.KeyBindings;
using Nickelony.QuickPick;
#if AVALONIAEDIT
using AvaloniaEdit;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using ICSharpCode.AvalonEdit;

namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the command palette of the sample: what it lists, what it runs, what it remembers, and the shortcuts that open it.</summary>
[STATestClass]
public sealed class SampleQuickPickTests
{
	private static QuickPickItem ItemOf(SampleWorkbench workbench, string commandId) =>
		workbench.QuickPick.Controller.State.Rows.Select(row => row.Item).OfType<QuickPickItem>().Single(item => item.Key == commandId);

	[TestMethod]
	public async Task OpenCommands_ListsEveryCommandWithItsTitleItsCategoryAndItsShortcut()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				workbench.QuickPick.OpenCommands();

				QuickPickItem save = ItemOf(workbench, SampleCommandIds.Save);

				Assert.AreEqual("Save", save.Label);
				Assert.AreEqual("File", save.Description);
				Assert.AreEqual("File", save.IconKey);
				Assert.AreEqual(SampleCommandIds.Save, save.Tag);
				Assert.AreEqual(workbench.KeyBindings.GetDisplayText(SampleCommandIds.Save), save.TrailingText);
				Assert.IsNotEmpty(save.TrailingText!);

				Assert.AreEqual("Command palette...", ItemOf(workbench, SampleCommandIds.CommandPalette).Label);
				Assert.AreEqual(workbench.Commands.Count, workbench.QuickPick.Controller.State.Rows.Count(row => row.Item is not null));

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task OpenCommands_TypingTheStartsOfWords_FindsTheCommandAsThePaletteOfVsCodeDoes()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				workbench.QuickPick.Controller.Open(">cp");

				Assert.AreEqual(SampleCommandIds.CommandPalette, workbench.QuickPick.Controller.State.ActiveItem!.Key);
				Assert.IsNotEmpty(workbench.QuickPick.Controller.State.ActiveItem!.LabelHighlights);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task OpenCommands_WordsInAnyOrder_FindTheCommandAndAQueryWithNoCommandSaysSo()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				foreach (string text in new[] { ">format doc", ">doc format" })
				{
					workbench.QuickPick.Controller.Open(text);
					Assert.AreEqual(SampleCommandIds.Format, workbench.QuickPick.Controller.State.ActiveItem!.Key);
				}

				workbench.QuickPick.Controller.Open(">zzz qqq");

				QuickPickPresentationState state = workbench.QuickPick.Controller.State;

				Assert.HasCount(1, state.Rows);
				Assert.AreEqual("No matching commands", state.Rows[0].Message);
				Assert.IsFalse(await workbench.QuickPick.Controller.AcceptAsync());
				Assert.IsTrue(workbench.QuickPick.Controller.State.IsOpen);
			}
		);
	}

	[TestMethod]
	public async Task OpenCommands_EveryCommandOfTheCatalogHasATitleAndACategory()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				Assert.IsTrue(
					workbench.KeyBindings.Descriptors.All(descriptor =>
						!string.IsNullOrWhiteSpace(descriptor.Title) && !string.IsNullOrWhiteSpace(descriptor.Category)
					)
				);

				Assert.DoesNotContain(descriptor => descriptor.Title!.Contains('_', StringComparison.Ordinal), workbench.KeyBindings.Descriptors);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task OpenCommands_DimsTheCommandsThatCannotRunNow()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.QuickPick.OpenCommands();

				// A tab is open, so the editing commands apply.
				Assert.IsTrue(ItemOf(workbench, SampleCommandIds.Format).IsEnabled);

				workbench.QuickPick.Controller.Dismiss();

				foreach (SamplePane pane in workbench.Panes.ToArray())
					await workbench.CloseAsync(pane);

				workbench.QuickPick.OpenCommands();

				// Without a tab, an editing command does not apply, while a command of the window does.
				Assert.IsFalse(ItemOf(workbench, SampleCommandIds.Format).IsEnabled);
				Assert.IsTrue(ItemOf(workbench, SampleCommandIds.ToggleTheme).IsEnabled);
			}
		);
	}

	[TestMethod]
	public async Task AcceptAsync_RunsTheCommandThroughTheCommandPath()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				string before = workbench.Theme.Name;

				workbench.QuickPick.Controller.Open(">toggle theme");

				Assert.AreEqual(SampleCommandIds.ToggleTheme, workbench.QuickPick.Controller.State.ActiveItem!.Key);
				Assert.IsTrue(await workbench.QuickPick.Controller.AcceptAsync());

				Assert.AreNotEqual(before, workbench.Theme.Name);
				Assert.IsFalse(workbench.QuickPick.Controller.State.IsOpen);
			}
		);
	}

	[TestMethod]
	public async Task AcceptAsync_ACommandThatCannotRunNow_IsNotAccepted()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				foreach (SamplePane pane in workbench.Panes.ToArray())
					await workbench.CloseAsync(pane);

				workbench.QuickPick.Controller.Open(">format document");

				Assert.IsFalse(await workbench.QuickPick.Controller.AcceptAsync());
				Assert.IsTrue(workbench.QuickPick.Controller.State.IsOpen);
			}
		);
	}

	[TestMethod]
	public async Task AcceptAsync_ListsTheCommandFirstTheNextTimeAndKeepsItBetweenRuns()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.QuickPick.Controller.Open(">toggle theme");
				await workbench.QuickPick.Controller.AcceptAsync();

				workbench.QuickPick.OpenCommands();

				QuickPickPresentationState state = workbench.QuickPick.Controller.State;

				Assert.AreEqual("recently used", state.Rows[0].SeparatorLabel);
				Assert.AreEqual(SampleCommandIds.ToggleTheme, state.Rows[1].Item!.Key);
				Assert.AreEqual(SampleCommandIds.ToggleTheme, state.ActiveItem!.Key);
				Assert.IsTrue(File.Exists(Path.Combine(directory.Path, SampleFiles.QuickPickRecentsFileName)));
			}
		);

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				workbench.QuickPick.OpenCommands();

				// A workbench over the same folder reads the commands the earlier one ran.
				Assert.AreEqual(SampleCommandIds.ToggleTheme, workbench.QuickPick.Controller.State.Rows[1].Item!.Key);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task Shortcuts_ControlShiftPAndF1_OpenThePaletteInTheCommandMode()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				KeyBindingPlatformProfile platform = KeyBindingPlatformProfile.ForCurrentPlatform;

				foreach (
					KeyStroke stroke in new[] { platform.PrimaryStroke(KeyCode.P, KeyModifiers.Shift), new KeyStroke(KeyCode.F1, KeyModifiers.None) }
				)
				{
					Assert.AreNotEqual(KeyStrokeDispatchOutcome.NotHandled, workbench.KeyBindings.Dispatch(stroke));
					Assert.IsTrue(workbench.QuickPick.Controller.State.IsOpen);
					Assert.AreEqual(">", workbench.QuickPick.Controller.State.Text);

					workbench.QuickPick.Controller.Dismiss();
				}

				return Task.CompletedTask;
			}
		);
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var stopwatch = Stopwatch.StartNew();

		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(15))
			await Task.Delay(20);

		Assert.IsTrue(condition(), "The quick pick did not reach the expected state in time.");
	}

	private static string[] LabelsOf(SampleWorkbench workbench) =>
		[.. workbench.QuickPick.Controller.State.Rows.Select(row => row.Item?.Label ?? $"--{row.SeparatorLabel}--")];

	private static async Task OpenFilesAsync(SampleWorkbench workbench)
	{
		workbench.QuickPick.OpenFiles();
		await WaitUntilAsync(() => workbench.QuickPick.Controller.State.Rows.Count > 0);
	}

	[TestMethod]
	public async Task OpenFiles_ListsTheFilesOfTheFolderAndLeavesTheExcludedFoldersOut()
	{
		using var directory = new TemporaryDirectory("sample-");

		foreach (string excluded in new[] { "bin/out.dll", "obj/cache.dll", ".git/config" })
		{
			string path = Path.Combine(directory.Path, excluded);

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, "x");
		}

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				await OpenFilesAsync(workbench);

				string[] labels = LabelsOf(workbench);

				Assert.Contains(SampleFiles.LuaFileName, labels);
				Assert.Contains("util.lua", labels);
				Assert.DoesNotContain("out.dll", labels);
				Assert.DoesNotContain("cache.dll", labels);
				Assert.DoesNotContain("config", labels);
				Assert.AreEqual("Type the name of a file to open", workbench.QuickPick.Controller.State.Placeholder);
			}
		);
	}

	[TestMethod]
	public async Task OpenFiles_ListsTheOpenDocumentsFirstAndMarksTheOneWithUnsavedChanges()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.SettingsPane.Editor.Text += "\n";

				await OpenFilesAsync(workbench);

				string[] labels = LabelsOf(workbench);
				QuickPickItem[] items = [.. workbench.QuickPick.Controller.State.Rows.Select(row => row.Item).OfType<QuickPickItem>()];

				Assert.AreSequenceEqual(["--recently opened--", SampleFiles.LuaFileName, SampleFiles.SettingsFileName], labels.Take(3));
				Assert.Contains("--file results--", labels);
				Assert.IsNull(items[0].TrailingText);
				Assert.AreEqual(OpenEditorsQuickPickOptions.Default.UnsavedText, items[1].TrailingText);
				Assert.IsNull(items[0].IconKey);
				Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, items[1].IconKey);
			}
		);
	}

	[TestMethod]
	public async Task AcceptAsync_OnAFile_OpensItInATabAndListsItFirstTheNextTimeAndBetweenRuns()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.QuickPick.Controller.Open("util");

				await WaitUntilAsync(() => workbench.QuickPick.Controller.State.ActiveItem?.Label == "util.lua");

				Assert.IsTrue(await workbench.QuickPick.Controller.AcceptAsync());

				SamplePane opened = workbench.Panes.Single(pane => pane.Title == "util.lua");

				Assert.AreSame(opened, workbench.ActivePane);

				await workbench.CloseAsync(opened);
				await OpenFilesAsync(workbench);

				Assert.AreSequenceEqual(
					["--recently opened--", "util.lua", SampleFiles.LuaFileName, SampleFiles.SettingsFileName],
					LabelsOf(workbench).Take(4)
				);

				Assert.IsTrue(File.Exists(Path.Combine(directory.Path, SampleFiles.QuickPickRecentsFileName)));
			}
		);

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				await OpenFilesAsync(workbench);

				Assert.AreSequenceEqual(
					["--recently opened--", "util.lua", SampleFiles.LuaFileName, SampleFiles.SettingsFileName],
					LabelsOf(workbench).Take(4)
				);
			}
		);
	}

	[TestMethod]
	public async Task AcceptAsync_OnAFileNameWithALineAndAColumn_OpensTheFileWithTheCaretThere()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.QuickPick.Controller.Open("util.lua:6:10");

				await WaitUntilAsync(() => workbench.QuickPick.Controller.State.ActiveItem?.Label == "util.lua");

				Assert.IsTrue(await workbench.QuickPick.Controller.AcceptAsync());

				SamplePane opened = workbench.Panes.Single(pane => pane.Title == "util.lua");

				Assert.AreSame(opened, workbench.ActivePane);
				Assert.AreEqual(6, opened.Editor.TextArea.Caret.Line);
				Assert.AreEqual(10, opened.Editor.TextArea.Caret.Column);
			}
		);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	public async Task OpenFiles_AFileCreatedWhileTheListIsOpen_ShowsUpWithoutReopeningIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				await OpenFilesAsync(workbench);

				Assert.DoesNotContain("fresh.lua", LabelsOf(workbench));

				await File.WriteAllTextAsync(Path.Combine(directory.Path, "fresh.lua"), "print('fresh')");
				await WaitUntilAsync(() => LabelsOf(workbench).Contains("fresh.lua"));
			}
		);
	}

	[TestMethod]
	public async Task OpenEditors_ListsTheTabsInOrderAndActivatesTheOneThatIsPicked()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				workbench.QuickPick.Controller.Open("edt ");

				Assert.AreSequenceEqual([SampleFiles.LuaFileName, SampleFiles.SettingsFileName], LabelsOf(workbench));
				Assert.AreSame(workbench.LuaPane, workbench.ActivePane);

				Assert.IsTrue(workbench.QuickPick.Controller.MoveActive(1));
				Assert.IsTrue(await workbench.QuickPick.Controller.AcceptAsync());

				Assert.AreSame(workbench.SettingsPane, workbench.ActivePane);
			}
		);
	}

	[TestMethod]
	public async Task GoToLine_PreviewsTheLineWhileTypingAndPutsTheCaretBackOnEscape()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				TextEditor editor = workbench.LuaPane.Editor;

				editor.CaretOffset = 0;
				workbench.QuickPick.OpenGoToLine();

				Assert.AreEqual(1, editor.TextArea.Caret.Line);

				workbench.QuickPick.Controller.Open(":3");

				Assert.AreEqual(3, editor.TextArea.Caret.Line);
				Assert.AreEqual(1, editor.TextArea.Caret.Column);

				workbench.QuickPick.Controller.Dismiss();

				Assert.AreEqual(1, editor.TextArea.Caret.Line);
				Assert.AreEqual(0, editor.CaretOffset);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task GoToLine_ALineAndAColumnBeyondTheDocument_ClampToIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				TextEditor editor = workbench.LuaPane.Editor;

				workbench.QuickPick.Controller.Open(":2:999");

				Assert.AreEqual(2, editor.TextArea.Caret.Line);
				Assert.AreEqual(editor.Document.GetLineByNumber(2).Length + 1, editor.TextArea.Caret.Column);

				workbench.QuickPick.Controller.Open(":99999");

				Assert.AreEqual(editor.Document.LineCount, editor.TextArea.Caret.Line);
				Assert.IsTrue(await workbench.QuickPick.Controller.AcceptAsync());
				Assert.AreEqual(editor.Document.LineCount, editor.TextArea.Caret.Line);
			}
		);
	}

	[TestMethod]
	public async Task GoToLine_AcceptKeepsTheCaretWhereThePreviewMovedIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				TextEditor editor = workbench.LuaPane.Editor;

				workbench.QuickPick.Controller.Open(":5:4");

				Assert.IsTrue(await workbench.QuickPick.Controller.AcceptAsync());

				Assert.AreEqual(5, editor.TextArea.Caret.Line);
				Assert.AreEqual(4, editor.TextArea.Caret.Column);
				Assert.IsFalse(workbench.QuickPick.Controller.State.IsOpen);
			}
		);
	}

	[TestMethod]
	public async Task GoToLine_WithoutATabOpen_ListsAHintAndAcceptsNothing()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				foreach (SamplePane pane in workbench.Panes.ToArray())
					await workbench.CloseAsync(pane);

				workbench.QuickPick.Controller.Open(":3");

				Assert.IsFalse(workbench.QuickPick.Controller.State.Rows.Single().Item!.IsEnabled);
				Assert.IsFalse(await workbench.QuickPick.Controller.AcceptAsync());
			}
		);
	}

	[TestMethod]
	public async Task QuestionMark_ListsEveryMode()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				workbench.QuickPick.Controller.Open("?");

				QuickPickItem[] modes = [.. workbench.QuickPick.Controller.State.Rows.Select(row => row.Item).OfType<QuickPickItem>()];

				Assert.AreSequenceEqual(
					[
						"Files",
						"Commands",
						"Open editors",
						"Go to line",
						"Go to symbol in editor",
						"Go to symbol in editor by category",
						"Go to symbol in workspace",
					],

					modes.Select(mode => mode.Label)
				);

				Assert.AreSequenceEqual([null, ">", "edt", ":", "@", "@:", "#"], modes.Select(mode => mode.TrailingText));

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task GoToSymbol_ListsTheOutlineOfTheActiveDocumentAndPreviewsTheSymbolUntilEscape()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				TextEditor editor = workbench.LuaPane.Editor;

				editor.CaretOffset = 0;
				workbench.QuickPick.OpenSymbols();

				QuickPickPresentationState state = workbench.QuickPick.Controller.State;
				string[] labels = [.. state.Rows.Select(row => row.Item).OfType<QuickPickItem>().Select(item => item.Label)];

				Assert.Contains("greet", labels);
				Assert.Contains("count_words", labels);
				Assert.Contains("GREETINGS", labels);
				Assert.StartsWith("symbols (", state.Rows[0].SeparatorLabel!);

				// The first symbol in the document is active, so its name is selected.
				Assert.AreEqual("util", editor.SelectedText);

				workbench.QuickPick.Controller.Open("@count_w");

				Assert.AreEqual("count_words", editor.SelectedText);
				Assert.AreEqual("symbol-function", workbench.QuickPick.Controller.State.ActiveItem!.IconKey);

				workbench.QuickPick.Controller.Dismiss();

				Assert.AreEqual(string.Empty, editor.SelectedText);
				Assert.AreEqual(0, editor.CaretOffset);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task GoToSymbol_AcceptKeepsTheSelectionOfTheSymbol()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				TextEditor editor = workbench.LuaPane.Editor;

				workbench.QuickPick.Controller.Open("@greet");

				Assert.IsTrue(await workbench.QuickPick.Controller.AcceptAsync());
				Assert.IsFalse(workbench.QuickPick.Controller.State.IsOpen);
				Assert.AreEqual("greet", editor.SelectedText);
			}
		);
	}

	[TestMethod]
	public async Task GoToSymbol_ByCategory_GroupsTheSymbolsByKindWithTheirNumber()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				workbench.QuickPick.Controller.Open("@:");

				string[] separators = [.. workbench.QuickPick.Controller.State.Rows.Select(row => row.SeparatorLabel).OfType<string>()];

				Assert.HasCount(2, separators);
				Assert.AreEqual("functions (2)", separators[0]);
				Assert.StartsWith("variables (", separators[1]);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task GoToSymbol_ForATabWithoutAnOutline_ListsAHint()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				workbench.ActivePane = workbench.SettingsPane;
				workbench.QuickPick.OpenSymbols();

				QuickPickItem hint = workbench.QuickPick.Controller.State.Rows.Select(row => row.Item).OfType<QuickPickItem>().Single();

				Assert.IsFalse(hint.IsEnabled);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task GoToWorkspaceSymbol_WithoutALanguageServer_FindsNothing()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				Assert.IsEmpty(workbench.WorkspaceSymbolProviders);

				workbench.QuickPick.Controller.Open("#greet");

				QuickPickPresentationState state = workbench.QuickPick.Controller.State;

				Assert.HasCount(1, state.Rows);
				Assert.AreEqual("No matching workspace symbols", state.Rows[0].Message);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task OpenSymbolAsync_ShowsTheDocumentOfTheSymbolWithTheCaretAtTheStartOfItsName()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				TextEditor editor = workbench.LuaPane.Editor;

				int offset = editor.Text.IndexOf("count_words", StringComparison.Ordinal);
				int line = editor.Text[..offset].Count(character => character == (char)10);
				int column = offset - (editor.Text.LastIndexOf((char)10, offset) + 1);

				var range = new TextPositionRange(new TextPosition(line, column), new TextPosition(line, column + 11));

				workbench.ActivePane = workbench.SettingsPane;

				await SampleQuickPick.OpenSymbolAsync(
					workbench,
					new WorkspaceSymbol("count_words", DocumentSymbolKind.Function, new WorkspaceSymbolLocation(workbench.Files.LuaPath, range))
				);

				Assert.AreSame(workbench.LuaPane, workbench.ActivePane);
				Assert.AreEqual(offset, editor.CaretOffset);
			}
		);
	}

	[TestMethod]
	public async Task Shortcuts_ControlShiftOAndControlTOpenTheSymbolModes()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				Assert.IsNotEmpty(workbench.KeyBindings.GetDisplayText(SampleCommandIds.GoToSymbol));
				Assert.IsNotEmpty(workbench.KeyBindings.GetDisplayText(SampleCommandIds.GoToWorkspaceSymbol));

				workbench.ExecuteCommand(SampleCommandIds.GoToSymbol);

				Assert.AreEqual("@", workbench.QuickPick.Controller.State.Text);

				workbench.ExecuteCommand(SampleCommandIds.GoToWorkspaceSymbol);

				Assert.AreEqual("#", workbench.QuickPick.Controller.State.Text);

				workbench.QuickPick.Controller.Dismiss();

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task Shortcuts_ControlPOpensTheFilesModeAndGoToLineIsBoundToControlG()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				KeyBindingPlatformProfile platform = KeyBindingPlatformProfile.ForCurrentPlatform;

				Assert.AreNotEqual(KeyStrokeDispatchOutcome.NotHandled, workbench.KeyBindings.Dispatch(platform.PrimaryStroke(KeyCode.P)));
				Assert.IsTrue(workbench.QuickPick.Controller.State.IsOpen);
				Assert.AreEqual(string.Empty, workbench.QuickPick.Controller.State.Text);

				workbench.QuickPick.Controller.Dismiss();

				// The command acts on the active editor, so its stroke only runs while an editor has the keyboard focus.
				Assert.IsNotEmpty(workbench.KeyBindings.GetDisplayText(SampleCommandIds.GoToLine));

				workbench.ExecuteCommand(SampleCommandIds.GoToLine);

				Assert.IsTrue(workbench.QuickPick.Controller.State.IsOpen);
				Assert.AreEqual(":", workbench.QuickPick.Controller.State.Text);

				workbench.QuickPick.Controller.Dismiss();

				return Task.CompletedTask;
			}
		);
	}
}
