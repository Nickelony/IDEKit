using System.IO;
using Nickelony.IDEKit.Core.Text;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SampleWindowNavigationTests
{
	private const string Script = "local function greet(name)\n    return name\nend\n\nlocal count = 1\nprint(greet(count))";

	[TestMethod]
	public async Task GoToDefinition_OfACalledFunction_MovesTheCaretToItsName()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = Script;
				pane.Editor.CaretOffset = Script.LastIndexOf("greet", StringComparison.Ordinal) + 2;

				Assert.IsTrue(await pane.Session.Language.Definitions.GoToDefinitionAsync());
				Assert.AreEqual(Script.IndexOf("greet", StringComparison.Ordinal), pane.Editor.CaretOffset);
			}
		);
	}

	[TestMethod]
	public async Task GoToDefinition_OfAParameter_MovesTheCaretToTheParameter()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = Script;
				pane.Editor.CaretOffset = Script.IndexOf("return name", StringComparison.Ordinal) + "return n".Length;

				Assert.IsTrue(await pane.Session.Language.Definitions.GoToDefinitionAsync());
				Assert.AreEqual(Script.IndexOf("name", StringComparison.Ordinal), pane.Editor.CaretOffset);
			}
		);
	}

	[TestMethod]
	public async Task GoToDefinition_OfAWordThatNothingDeclares_LeavesTheCaret()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = Script;
				int caret = Script.IndexOf("print", StringComparison.Ordinal) + 1;
				pane.Editor.CaretOffset = caret;

				Assert.IsFalse(await pane.Session.Language.Definitions.GoToDefinitionAsync());
				Assert.AreEqual(caret, pane.Editor.CaretOffset);
			}
		);
	}

	[TestMethod]
	public async Task FindReferences_ListsEveryOccurrence_AndARowSelectsItsRange()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = Script;
				pane.Editor.CaretOffset = Script.IndexOf("count", StringComparison.Ordinal);

				await window.Workbench.FindReferencesAsync(pane);

				IReadOnlyList<SampleLocation> references = window.Workbench.Panels.References;

				Assert.HasCount(2, references);
				Assert.StartsWith("sample.lua:6:13", references[1].Text);

				Assert.IsTrue(await window.Workbench.NavigateToAsync(references[1]));
				Assert.AreEqual(Script.LastIndexOf("count", StringComparison.Ordinal), pane.Editor.SelectionStart);
				Assert.AreEqual("count".Length, pane.Editor.SelectionLength);
			}
		);
	}

	[TestMethod]
	public async Task Outline_ListsTheFunctionsWithTheirParameters_AndTheFilterNarrowsIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = Script;

				await pane.Session.Language.Outline.RefreshAsync();

				SamplePanels panels = window.Workbench.Panels;

				Assert.AreSequenceEqual(
					["greet (name)  [Function]", "   name  [Variable]", "count  [Variable]"],
					[.. panels.Outline.Select(row => row.Text)]
				);

				panels.OutlineFilter = "COU";

				Assert.HasCount(1, panels.Outline);
				Assert.AreEqual(new TextRange(Script.IndexOf("count", StringComparison.Ordinal), "count".Length), panels.Outline[0].Range);
			}
		);
	}

	[TestMethod]
	public async Task Problems_FollowTheActivePane()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane settings = window.Workbench.SettingsPane;
				settings.Editor.Text = "{\n  \"tabSize\": 4   \n}";

				await SampleTestHost.WaitUntilAsync(
					() => settings.Session.Language.Diagnostics?.Current.Any(diagnostic => diagnostic.Code == "trailing-whitespace") == true,
					"The trailing-whitespace diagnostic did not arrive."
				);

				window.Workbench.ActivePane = settings;

				SampleLocation problem = window.Workbench.Panels.Problems.Single();

				Assert.AreEqual(settings.FilePath, problem.DocumentId);
				Assert.Contains("Trailing whitespace", problem.Text);
			}
		);
	}

	[TestMethod]
	public async Task NavigateTo_ARowOfAnotherDocument_ShowsThePaneOfThatDocument()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane settings = window.Workbench.SettingsPane;

				Assert.IsTrue(await window.Workbench.NavigateToAsync(new SampleLocation("row", settings.FilePath, new TextRange(4, 7))));
				Assert.AreSame(settings, window.Workbench.ActivePane);
				Assert.AreEqual(4, settings.Editor.CaretOffset);
				Assert.AreEqual(7, settings.Editor.SelectionLength);
			}
		);
	}

	[TestMethod]
	public async Task NavigateTo_AFileThatNoTabShows_OpensIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				string utilPath = Path.Combine(directory.Path, "lib", "util.lua");

				Assert.IsTrue(await window.Workbench.NavigateToAsync(new SampleLocation("row", utilPath, new TextRange(0, 2))));
				Assert.AreEqual(utilPath, window.Workbench.ActivePane?.FilePath);
				Assert.HasCount(3, window.Workbench.Panes);
			}
		);
	}

	[TestMethod]
	public async Task NavigateTo_AMissingFile_ReportsIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				string? status = null;
				window.Workbench.StatusChanged += (_, e) => status = e.Message;

				Assert.IsFalse(await window.Workbench.NavigateToAsync(new SampleLocation("row", Path.Combine(directory.Path, "other.lua"), default)));
				Assert.AreEqual("other.lua does not exist", status);
			}
		);
	}
}
