using System.IO;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.KeyBindings;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SampleWindowTests
{
	private static KeyStroke Control(KeyCode key) => new(key, KeyModifiers.Control);

	[TestMethod]
	public async Task MainWindow_OpensBothDocumentsAndShowsCompletionForATypedWord()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = "pri";
				pane.Editor.CaretOffset = 3;

				SampleCompletion? completion = pane.Session.Language.Completion;
				Assert.IsNotNull(completion);

				bool shown = await completion.RequestAsync();

				Assert.IsTrue(shown);
				Assert.IsTrue(completion.Controller.CurrentPresentation.IsListVisible);
				Assert.HasCount(2, window.Workbench.Panes);
			}
		);
	}

	[TestMethod]
	public async Task CommandPalette_OpensInTheMainWindowAndRunsTheCommandOfTheActiveRow()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleQuickPick palette = window.Workbench.QuickPick;
				string theme = window.Workbench.Theme.Name;

				palette.Controller.Open(">toggle theme");

				Assert.IsTrue(palette.IsWindowOpen);
				Assert.IsTrue(await palette.Controller.AcceptAsync());
				Assert.IsFalse(palette.IsWindowOpen);
				Assert.AreNotEqual(theme, window.Workbench.Theme.Name);
			}
		);
	}

	[TestMethod]
	public async Task CommandPalette_EscapeClosesTheWindowWithoutRunningACommand()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				SampleQuickPick palette = window.Workbench.QuickPick;
				string theme = window.Workbench.Theme.Name;

				palette.OpenCommands();
				palette.Controller.Dismiss();

				Assert.IsFalse(palette.IsWindowOpen);
				Assert.AreEqual(theme, window.Workbench.Theme.Name);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task Diagnostics_OfTheSampleFile_ReportTheMarkerAndTheTrailingWhitespace()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				DiagnosticsController? diagnostics = window.Workbench.LuaPane.Session.Language.Diagnostics;
				Assert.IsNotNull(diagnostics);

				window.Workbench.LuaPane.Editor.Text = "-- TODO: fix this   \nprint(1)";

				await SampleTestHost.WaitUntilAsync(
					() => diagnostics.Current.Any(diagnostic => diagnostic.Code == "trailing-whitespace"),
					"The diagnostics did not arrive."
				);

				Assert.Contains(diagnostic => diagnostic.Code == "marker", diagnostics.Current);
				Assert.Contains(diagnostic => diagnostic.Code == "trailing-whitespace", diagnostics.Current);
			}
		);
	}

	[TestMethod]
	public async Task SignatureHelp_InsideACallOfALibraryFunction_ShowsThePopup()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = "string.format(";
				pane.Editor.CaretOffset = pane.Editor.Text.Length;

				// The popup shows over an active window only, as it does for a user who types into the window.
				window.Activate();
				await SampleTestHost.WaitUntilAsync(() => window.IsActive, "The window did not activate.");

				Assert.IsTrue(await pane.Session.Language.SignatureHelp.RequestAsync());
				Assert.IsTrue(pane.Session.Language.SignatureHelp.IsVisible);
			}
		);
	}

	[TestMethod]
	public async Task CodeActions_OnALineWithTrailingWhitespace_OpenTheMenu()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = "print(1)   ";
				pane.Editor.CaretOffset = 3;

				Assert.IsTrue(await (pane.Session.Language.CodeActions?.OpenMenuAsync() ?? Task.FromResult(false)));
			}
		);
	}

	[TestMethod]
	public async Task Save_WritesTheTextAndClearsTheChangeMarkers()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.SettingsPane;
				pane.Editor.Text = pane.Editor.Text.Replace("\"fontSize\": 14", "\"fontSize\": 18", StringComparison.Ordinal);

				Assert.IsNotEmpty(pane.Session.LineStatus.ChangedLines);

				await window.Workbench.SaveAsync(pane);

				Assert.IsEmpty(pane.Session.LineStatus.ChangedLines);
				Assert.Contains("\"fontSize\": 18", File.ReadAllText(pane.FilePath));
				Assert.AreEqual(18, pane.Editor.FontSize);
			}
		);
	}

	[TestMethod]
	public async Task Format_WithoutAFormattingProvider_TrimsTheTrailingWhitespace()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = "print(1)   \nprint(2)  ";
				window.Workbench.ActivePane = pane;

				Assert.IsFalse(pane.Session.Language.Formatting.CanFormat);

				window.Workbench.Commands.First(command => command.Id == SampleCommandIds.Format).Execute();

				Assert.AreEqual("print(1)\nprint(2)", pane.Editor.Text);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task TheTrimCommand_ActsOnTheActivePane()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				SamplePane lua = window.Workbench.LuaPane;
				SamplePane settings = window.Workbench.SettingsPane;

				lua.Editor.Text = "print(1)   ";
				settings.Editor.Text = "{}   ";
				window.Workbench.ActivePane = settings;

				window.Workbench.Commands.First(command => command.Id == SampleCommandIds.TrimWhitespace).Execute();

				Assert.AreEqual("{}", settings.Editor.Text);
				Assert.AreEqual("print(1)   ", lua.Editor.Text);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task SavingTheSettings_SwitchesTheThemeAndTheJsonHighlighting()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.SettingsPane;
				pane.Editor.Text = pane.Editor.Text.Replace("\"Light\"", "\"Dark\"", StringComparison.Ordinal);

				await window.Workbench.SaveAsync(pane);

				Assert.AreEqual("Dark", window.Workbench.Theme.Name);
				Assert.Contains("\"Dark\"", File.ReadAllText(pane.FilePath));

				window.Workbench.ToggleTheme();

				Assert.AreEqual("Light", window.Workbench.Theme.Name);
			}
		);
	}

	[TestMethod]
	public async Task SavingSettingsWithAnInvalidTabSize_KeepsTheDefault()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				// A tab that shows no indentation takes the tab size of the settings.
				SamplePane lua = window.Workbench.LuaPane;
				lua.Editor.Text = "print(1)";

				SamplePane pane = window.Workbench.SettingsPane;
				pane.Editor.Text = pane.Editor.Text.Replace("\"tabSize\": 4", "\"tabSize\": 0", StringComparison.Ordinal);

				await window.Workbench.SaveAsync(pane);

				Assert.AreEqual(SampleSettings.Default.TabSize, lua.Editor.Options.IndentationSize);
			}
		);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	public async Task ChangeOnDisk_ReplacesTheEditorTextOfAnUnchangedDocument()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;

				await File.WriteAllTextAsync(pane.FilePath, "print('changed on disk')\n");

				await SampleTestHost.WaitUntilAsync(
					() => pane.Editor.Text.Contains("changed on disk", StringComparison.Ordinal),
					"The coordinator did not reload the file."
				);
			}
		);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	public async Task ChangeOnDisk_KeepsAnEditorThatOnlyDeletedLines()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				string edited = string.Join('\n', pane.Editor.Text.Split('\n').Skip(2));

				pane.Editor.Text = edited;

				// Deleted lines mark no line, so the unsaved state is the text compared with the saved text.
				Assert.IsEmpty(pane.Session.LineStatus.ChangedLines);

				string? status = null;
				window.Workbench.StatusChanged += (_, e) => status = e.Message;

				await File.WriteAllTextAsync(pane.FilePath, "print('changed on disk')\n");

				await SampleTestHost.WaitUntilAsync(() => status is not null, "The change on disk was not handled.");

				Assert.AreEqual(edited, pane.Editor.Text);
			}
		);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	public async Task Disposal_WhileAReloadIsPending_Completes()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				await File.WriteAllTextAsync(window.Workbench.LuaPane.FilePath, "print('changed on disk')\n");
				await window.Workbench.DisposeAsync();
			}
		);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	public async Task RunCommand_PublishesTheOutputOfTheProcess()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				string? output = null;
				window.Workbench.OutputChanged += (_, e) => output = e.Output;

				await window.Workbench.RunAsync();

				Assert.IsNotNull(output);
				Assert.Contains("Exit code 0", output);
			}
		);
	}

	[TestMethod]
	public async Task Rebind_ToAFreeShortcut_ChangesTheDisplayTextAndWritesTheOverrides()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				SampleKeyBindings keys = window.Workbench.KeyBindings;
				string before = keys.GetDisplayText(SampleCommandIds.FindNext);

				window.Workbench.KeyBindings.Rebind(SampleCommandIds.FindNext, "Control+Shift+G");

				Assert.AreNotEqual(before, keys.GetDisplayText(SampleCommandIds.FindNext));
				Assert.IsTrue(File.Exists(Path.Combine(directory.Path, SampleFiles.KeyBindingsFileName)));

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task AChord_ThatIsBoundToAWorkbenchCommand_WorksInTheSettingsPane()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				window.Workbench.ActivePane = window.Workbench.SettingsPane;

				SampleKeyBindings keys = window.Workbench.KeyBindings;
				string before = window.Workbench.Theme.Name;

				Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, keys.Dispatch(Control(KeyCode.K)));
				Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, keys.Dispatch(Control(KeyCode.T)));
				Assert.AreNotEqual(before, window.Workbench.Theme.Name);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task TheCommentShortcut_IsLeftToTheSettingsFileEditor()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				window.Workbench.ActivePane = window.Workbench.SettingsPane;

				Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, window.Workbench.KeyBindings.Dispatch(Control(KeyCode.Slash)));

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task AMissingLuaServer_ReportsTheFailedStartInTheStatusLine()
	{
		using var directory = new TemporaryDirectory("sample-");
		var options = new SampleServerOptions(Path.Combine(directory.Path, "no-such-server.exe"), RoslynServerPath: null);

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				List<string> statuses = [];
				window.Workbench.StatusChanged += (_, e) => statuses.Add(e.Message);

				// The provider starts the server on its first request, and the missing executable makes that start fail.
				_ = window.Workbench.LuaPane.Session.Language.SignatureHelp.RequestAsync();
				_ = window.Workbench.LuaPane.Session.Language.Completion?.RequestAsync();

				await SampleTestHost.WaitUntilAsync(() => statuses.Count > 0, "No start failure was reported.");

				Assert.Contains(
					status =>
						status.Contains("no-such-server", StringComparison.OrdinalIgnoreCase)
						|| status.Contains("start", StringComparison.OrdinalIgnoreCase),

					statuses
				);
			},

			options
		);
	}

	[TestMethod]
	public async Task Rename_OfASymbolAtTheCaret_ChangesEveryOccurrenceThroughTheCoordinator()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = "local total = 1\nprint(total)";
				pane.Editor.CaretOffset = "local to".Length;

				await window.Workbench.RenameAsync(pane, "sum");

				Assert.AreEqual("local sum = 1\nprint(sum)", pane.Editor.Text);
				Assert.IsFalse(pane.View.HasPendingEdits);
			}
		);
	}

	[TestMethod]
	public async Task ClosingTheWindowWithUnsavedText_AsksFirst_AndCancelKeepsTheWindowOpen()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				int asked = 0;

				window.Workbench.DecideSaveAsync = _ =>
				{
					asked++;
					return Task.FromResult(SampleSaveDecision.Cancel);
				};

				window.Workbench.LuaPane.Editor.Text += "-- typed\n";

				window.Close();
				await Task.Delay(300);

				Assert.AreEqual(1, asked);
				Assert.IsTrue(window.IsVisible, "The window closed although the answer was Cancel.");
				Assert.IsTrue(window.Workbench.LuaPane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task LineTerminatorStatus_WhenTheOtherViewOfASplitFollowsAnUndo_ShowsTheStyleOfTheDocumentAlready()
	{
		using var directory = new TemporaryDirectory("sample-");
		string path = Path.Combine(directory.Path, "lines.txt");
		File.WriteAllText(path, "one\ntwo\nthree\n");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;

				SamplePane first = await workbench.OpenAsync(path);
				SamplePane split = (await workbench.SplitAsync(first))!;

				workbench.ActivePane = split;
				split.Editor.Document.UndoStack.ClearAll();
				await workbench.UseLineTerminatorAsync(LineTerminatorStyle.CrLf);

				await SampleTestHost.WaitUntilAsync(
					() => window.LineTerminatorText == "CRLF" && !first.View.HasPendingEdits && !split.View.HasPendingEdits,
					"The conversion did not settle."
				);

				// The window subscribed to the text of the other view before this test did, so the label it shows when the text changes is the one the window refreshed.
				string? labelWhenTheOtherViewChanged = null;
				first.Editor.TextChanged += (_, _) => labelWhenTheOtherViewChanged ??= window.LineTerminatorText;

				split.Editor.Document.UndoStack.Undo();
				await SampleTestHost.WaitUntilAsync(() => first.Editor.Text == "one\ntwo\nthree\n", "The other view did not follow the undo.");

				Assert.AreEqual("LF", labelWhenTheOtherViewChanged);
				Assert.AreEqual("LF", window.LineTerminatorText);
			}
		);
	}
}
