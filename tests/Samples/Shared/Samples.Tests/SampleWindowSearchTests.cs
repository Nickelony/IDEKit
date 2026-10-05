#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SampleWindowSearchTests
{
	private const string Script = "local total = 1\nprint(total)\nprint(Total)";

	[TestMethod]
	public async Task FindAll_ListsEveryMatchOfTheActivePaneInTheSearchResults()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				window.Workbench.LuaPane.Editor.Text = Script;
				window.Workbench.Search = new SampleSearchOptions("total");

				window.Workbench.FindAll();

				// The search ignores case unless it matches case, so the capitalized occurrence is a match too.
				Assert.AreSequenceEqual(
					["sample.lua:1:7  local total = 1", "sample.lua:2:7  print(total)", "sample.lua:3:7  print(Total)"],
					[.. window.Workbench.Panels.SearchResults.Select(row => row.Text)]
				);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task FindAll_MatchingCase_LeavesOutTheOtherCase()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				window.Workbench.LuaPane.Editor.Text = Script;
				window.Workbench.Search = new SampleSearchOptions("total", MatchCase: true);

				window.Workbench.FindAll();

				Assert.HasCount(2, window.Workbench.Panels.SearchResults);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task FindPrevious_FromTheStart_WrapsToTheLastMatch()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = Script;
				pane.Editor.CaretOffset = 0;
				window.Workbench.Search = new SampleSearchOptions("print");

				Assert.IsTrue(window.Workbench.FindPrevious());
				Assert.AreEqual(Script.LastIndexOf("print", StringComparison.Ordinal), pane.Editor.SelectionStart);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task NextProblem_AfterALineIsTypedAboveTheProblem_SelectsItsMovedText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				window.Workbench.ActivePane = pane;
				pane.Editor.Text = "prnt(1)";
				pane.Editor.CaretOffset = 0;

				await SampleTestHost.WaitUntilAsync(
					() => pane.Session.Language.Diagnostics?.Current is [{ Range.Offset: 0 }],
					"The diagnostic did not arrive."
				);

				// Before the pull that the edit schedules answers, the row and Next problem follow the moved underline.
				pane.Editor.Document.Insert(0, "local a = 1\n");

				Assert.AreEqual(12, window.Workbench.Panels.Problems.Single().Range.Offset);
				Assert.IsTrue(window.Workbench.GoToNextProblem());
				Assert.AreEqual("prnt", pane.Editor.SelectedText);
			}
		);
	}

	[TestMethod]
	public async Task NextProblem_SelectsTheProblemsAfterTheCaretInTurn_AndWraps()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				pane.Editor.Text = "-- TODO first\nprnt(1)";
				pane.Editor.CaretOffset = 0;

				// The starter file's diagnostics come first, so the wait is for the "prnt" of the new text.
				await SampleTestHost.WaitUntilAsync(
					() => pane.Session.Language.Diagnostics?.Current.Any(diagnostic => diagnostic.Range.Offset == 14) == true,
					"The diagnostics did not arrive."
				);

				Assert.IsTrue(window.Workbench.GoToNextProblem());
				Assert.AreEqual("TODO", pane.Editor.SelectedText);

				Assert.IsTrue(window.Workbench.GoToNextProblem());
				Assert.AreEqual("prnt", pane.Editor.SelectedText);

				Assert.IsTrue(window.Workbench.GoToNextProblem());
				Assert.AreEqual("TODO", pane.Editor.SelectedText);
			}
		);
	}
}
