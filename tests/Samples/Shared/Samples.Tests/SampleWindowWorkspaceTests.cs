using System.IO;
using Nickelony.KeyBindings;
#if AVALONIAEDIT
using Avalonia.Media;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using System.Windows;

namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Drives the explorer, the tabs, the second views of a document and the file operations of the sample through its real window.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SampleWindowWorkspaceTests
{
	[TestMethod]
	public async Task Explorer_ShowsTheFoldersFirst_ThenTheFiles()
	{
		using var directory = new TemporaryDirectory("sample-");
		File.WriteAllText(Path.Combine(directory.Path, ".hidden"), "x");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				Assert.AreSequenceEqual(
					["lib", "README.md", "Sample.cs", "Sample.csproj", "sample.lua", "Sample.vb", "settings.json"],
					[.. window.Workbench.Explorer.Root.Children.Select(node => node.Name)]
				);

				Assert.AreEqual("util.lua", window.Workbench.Explorer.Root.Children[0].Children.Single().Name);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task Explorer_RevealsTheFileOfTheActiveTab_OpeningTheFoldersAboveIt()
	{
		using var directory = new TemporaryDirectory("sample-");
		string nested = Path.Combine(path1: directory.Path, path2: "lib", path3: "deep", path4: "nested.lua");
		Directory.CreateDirectory(Path.Combine(directory.Path, "lib", "deep"));
		File.WriteAllText(nested, "print(1)");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				await window.Workbench.OpenAsync(nested);

				Assert.AreEqual(nested, SampleWindowTestHelpers.SelectedExplorerPath(window));
				Assert.IsTrue(SampleWindowTestHelpers.SelectedExplorerRowIsInAnExpandedFolder(window));
			}
		);
	}

	[TestMethod]
	public async Task Open_AFileTwice_ShowsOneTab_AndCloseRemovesIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				string readme = Path.Combine(directory.Path, "README.md");

				SamplePane first = await workbench.OpenAsync(readme);
				SamplePane second = await workbench.OpenAsync(readme);

				Assert.AreSame(first, second);
				Assert.HasCount(3, workbench.Panes);
				Assert.AreEqual(SampleDocumentKind.Markdown, first.Session.Kind);

				Assert.IsTrue(await workbench.CloseAsync(first));
				Assert.HasCount(2, workbench.Panes);
				Assert.AreNotSame(first, workbench.ActivePane);
			}
		);
	}

	[TestMethod]
	[DataRow("Cancel", true, "print(1)")]
	[DataRow("Discard", false, "print(1)")]
	[DataRow("Save", false, "print(2)")]
	public async Task Close_ATabWithUnsavedChanges_FollowsTheAnswer(string answer, bool staysOpen, string textOnDisk)
	{
		using var directory = new TemporaryDirectory("sample-");
		string path = Path.Combine(directory.Path, "notes.lua");
		File.WriteAllText(path, "print(1)");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				SamplePane pane = await workbench.OpenAsync(path);

				pane.Editor.Text = "print(2)";

				int asked = 0;

				workbench.DecideSaveAsync = _ =>
				{
					asked++;
					return Task.FromResult(Enum.Parse<SampleSaveDecision>(answer));
				};

				Assert.AreEqual(!staysOpen, await workbench.CloseAsync(pane));
				Assert.AreEqual(1, asked);
				Assert.AreEqual(staysOpen, workbench.Panes.Contains(pane));
				Assert.AreEqual(textOnDisk, File.ReadAllText(path));
			}
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Close_ATabWhoseSaveFails_StaysOpen()
	{
		using var directory = new TemporaryDirectory("sample-");
		string path = Path.Combine(directory.Path, "notes.lua");
		File.WriteAllText(path, "print(1)");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				SamplePane pane = await workbench.OpenAsync(path);

				pane.Editor.Text = "print(2)";
				workbench.DecideSaveAsync = _ => Task.FromResult(SampleSaveDecision.Save);

				// A reader that does not share deletion makes the replace fail on every attempt.
				using (new FileStream(path, mode: FileMode.Open, access: FileAccess.Read, share: FileShare.Read))
					Assert.IsFalse(await workbench.CloseAsync(pane));

				Assert.Contains(pane, workbench.Panes);
				Assert.AreEqual("print(1)", File.ReadAllText(path));
				Assert.IsTrue(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task Split_TypingInEitherView_ReachesTheOtherOnceTypingPauses()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				SamplePane? split = await window.Workbench.SplitAsync(pane);

				Assert.IsNotNull(split);
				Assert.AreEqual("sample.lua (2)", split.Title);

				split.Editor.Document.Insert(0, "-- typed in the split\n");

				await SampleTestHost.WaitUntilAsync(
					() => pane.Editor.Text.StartsWith("-- typed in the split", StringComparison.Ordinal),
					"The first view did not follow."
				);

				pane.Editor.Document.Insert(0, "-- typed in the first\n");

				await SampleTestHost.WaitUntilAsync(
					() => split.Editor.Text.StartsWith("-- typed in the first", StringComparison.Ordinal),
					"The split did not follow."
				);

				Assert.AreEqual(pane.Editor.Text, split.Editor.Text);
			}
		);
	}

	[TestMethod]
	public async Task Split_TheFollowingView_KeepsItsCaretOutsideTheChange()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				SamplePane? split = await window.Workbench.SplitAsync(pane);

				Assert.IsNotNull(split);

				// The caret sits before the change, so a view that replaced its whole text would move it to the end.
				pane.Editor.CaretOffset = 10;
				split.Editor.Document.Insert(split.Editor.Document.TextLength, "\n-- appended");

				await SampleTestHost.WaitUntilAsync(
					() => pane.Editor.Text.EndsWith("-- appended", StringComparison.Ordinal),
					"The first view did not follow."
				);

				Assert.AreEqual(10, pane.Editor.CaretOffset);
			}
		);
	}

	[TestMethod]
	public async Task Split_AndPreview_ShowBesideTheirFirstView_UntilItCloses()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				SamplePane pane = workbench.LuaPane;
				SamplePane? split = await workbench.SplitAsync(pane);

				SampleMarkdownPreview? preview = await workbench.OpenPreviewAsync(
					await workbench.OpenAsync(Path.Combine(directory.Path, "README.md"))
				);

				Assert.IsNotNull(split);
				Assert.IsNotNull(preview);
				Assert.AreSequenceEqual([split, preview], [.. workbench.TabsBeside]);
				Assert.AreNotSame(SampleWindowTestHelpers.TabGroupOf(pane), SampleWindowTestHelpers.TabGroupOf(split));

				// The split is the only view of its document once the first one closes, so it moves to the first group.
				Assert.IsTrue(await workbench.CloseAsync(pane));
				Assert.AreSequenceEqual([preview], [.. workbench.TabsBeside]);

				Assert.AreSame(SampleWindowTestHelpers.TabGroupOf(workbench.SettingsPane), SampleWindowTestHelpers.TabGroupOf(split));
			}
		);
	}

	[TestMethod]
	public async Task Preview_OfAMarkdownFile_FollowsItsEditor()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane readme = await window.Workbench.OpenAsync(Path.Combine(directory.Path, "README.md"));
				SampleMarkdownPreview? preview = await window.Workbench.OpenPreviewAsync(readme);

				Assert.IsNotNull(preview);
				object? first = preview.Content.Child;
				Assert.IsNotNull(first);

				readme.Editor.Document.Insert(0, "# A new heading\n\n");
				await SampleTestHost.WaitUntilAsync(() => preview.Content.Child != first, "The preview did not render the edit.");

				window.Workbench.ClosePreview(preview);
				Assert.IsEmpty(window.Workbench.Previews);
			}
		);
	}

	[TestMethod]
	public async Task Preview_OfAFileThatIsNotMarkdown_IsRefused()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,
			async window => Assert.IsNull(await window.Workbench.OpenPreviewAsync(window.Workbench.LuaPane))
		);
	}

	[TestMethod]
	public async Task RenameEntry_OfAnOpenFile_MovesTheFile_AndTheTabFollowsWithItsEditor()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				string utilPath = Path.Combine(directory.Path, "lib", "util.lua");
				SamplePane util = await window.Workbench.OpenAsync(utilPath);

				util.Editor.CaretOffset = 5;

				await window.Workbench.RenameEntryAsync(utilPath, "helpers.lua");

				string renamed = Path.Combine(directory.Path, "lib", "helpers.lua");

				await SampleTestHost.WaitUntilAsync(() => window.Workbench.ActivePane?.FilePath == renamed, "The tab did not follow the rename.");

				Assert.IsTrue(File.Exists(renamed));
				Assert.IsFalse(File.Exists(utilPath));
				Assert.AreEqual("helpers.lua", window.Workbench.ActivePane?.Title);
				Assert.AreEqual(5, window.Workbench.ActivePane?.Editor.CaretOffset);

				// The tab keeps its editor, so the undo history, the scroll position and the folds survive the rename.
				Assert.AreSame(util, window.Workbench.ActivePane);
				Assert.AreSame(util.Editor, window.Workbench.ActivePane?.Editor);
			}
		);
	}

	[TestMethod]
	public async Task RenameEntry_OfAFolder_MovesTheTabsOfItsFiles()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				await window.Workbench.OpenAsync(Path.Combine(directory.Path, "lib", "util.lua"));

				await window.Workbench.RenameEntryAsync(Path.Combine(directory.Path, "lib"), "library");

				string moved = Path.Combine(directory.Path, "library", "util.lua");

				await SampleTestHost.WaitUntilAsync(
					() => window.Workbench.Panes.Any(pane => pane.FilePath == moved),
					"The tab did not follow the folder."
				);

				Assert.IsTrue(File.Exists(moved));
				Assert.AreEqual("library", window.Workbench.Explorer.Root.Children[0].Name);
			}
		);
	}

	[TestMethod]
	public async Task DeleteEntry_OfAnOpenFile_MovesItToTheTrash_AndClosesItsTab()
	{
		using var directory = new TemporaryDirectory("sample-");
		string trash = $"{directory.Path}.Trash";

		try
		{
			await SampleTestHost.RunWindowAsync(
				directory,

				async window =>
				{
					string readme = Path.Combine(directory.Path, "README.md");
					await window.Workbench.OpenAsync(readme);

					await window.Workbench.DeleteEntryAsync(readme);

					Assert.IsFalse(File.Exists(readme));
					Assert.DoesNotContain(pane => pane.FilePath == readme, window.Workbench.Panes);
					Assert.ContainsSingle(Directory.GetFiles(trash, "README.md", SearchOption.AllDirectories));
				}
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(trash);
		}
	}

	[TestMethod]
	public async Task CreateFile_WritesAnEmptyFile_AndOpensIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				await window.Workbench.CreateFileAsync(Path.Combine(directory.Path, "lib"), "more.lua");

				string path = Path.Combine(directory.Path, "lib", "more.lua");

				Assert.IsTrue(File.Exists(path));
				Assert.AreEqual(path, window.Workbench.ActivePane?.FilePath);
				Assert.AreEqual(SampleDocumentKind.Lua, window.Workbench.ActivePane?.Session.Kind);
			}
		);
	}

	[TestMethod]
	public async Task FindInFiles_ListsTheMatchesOfEveryFile_ReadingAnOpenOneFromItsEditor()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				window.Workbench.LuaPane.Editor.Document.Insert(0, "-- shout here too\n");
				window.Workbench.Search = new SampleSearchOptions("shout", MatchCase: true);

				window.Workbench.FindInFiles();

				string[] files = [.. window.Workbench.Panels.SearchResults.Select(row => row.Text.Split(':')[0]).Distinct()];

				Assert.AreSequenceEqual(["lib/util.lua", "sample.lua"], files, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);

				Assert.StartsWith(
					"sample.lua:1:4",
					window.Workbench.Panels.SearchResults.First(row => row.Text.StartsWith("sample.lua", StringComparison.Ordinal)).Text
				);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task KeyBindings_AWindowCommandRunsWithoutAFocusedEditor_AndAnEditorCommandDoesNot()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				SampleKeyBindings keyBindings = window.Workbench.KeyBindings;

				// The headless window gives no editor the keyboard focus, so only the window tier answers.
				Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, keyBindings.Dispatch(new KeyStroke(KeyCode.Slash, KeyModifiers.Control)));
				Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, keyBindings.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control)));

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task TheStatusLine_CutsALongMessageWithAnEllipsis_AndTheToolTipHoldsTheWholeOfIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				string message = "Failed: LoadFailed (InvalidEncoding): the store could not open " + new string('x', 400);

				window.Workbench.Report(message);

				Assert.AreEqual(TextTrimming.CharacterEllipsis, window.StatusLine.TextTrimming);
				Assert.AreEqual(message, window.StatusLine.Text);
				Assert.AreEqual(message, SampleWindowTestHelpers.StatusLineToolTip(window));

				return Task.CompletedTask;
			}
		);
	}
}
