using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>
/// Drives an edit that a language server asks for through the real window, as the server does while it runs the command of a quick fix.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SampleWindowServerEditTests
{
	private static WorkspaceEdit Insert(string documentId, int line, string text) =>
		new([new TextDocumentEdit(documentId, [new TextEdit(new TextPositionRange(new TextPosition(line, 0), new TextPosition(line, 0)), text)])]);

	[TestMethod]
	public async Task ApplyServerEditAsync_FromAConnectionThread_ChangesTheOpenDocumentAndReportsItApplied()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				SamplePane pane = workbench.LuaPane;
				string before = pane.Editor.Text;

				// The language server asks on a thread of its own, and the documents belong to the UI thread.
				bool applied = await Task.Run(() => workbench.ApplyServerEditAsync(Insert(pane.FilePath, 0, "-- from the server\n")));

				Assert.IsTrue(applied);
				Assert.AreEqual("-- from the server\n" + before, pane.Editor.Text);
			}
		);
	}

	[TestMethod]
	public async Task ApplyServerEditAsync_ForAnEditTheDocumentsRefuse_AnswersNotAppliedAndLeavesTheText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				SamplePane pane = workbench.LuaPane;
				string before = pane.Editor.Text;

				var reports = new List<string>();

				workbench.StatusChanged += (_, e) => reports.Add(e.Message);

				// A document that does not exist is a plan issue, so nothing changes.
				bool applied = await Task.Run(() =>
					workbench.ApplyServerEditAsync(Insert(System.IO.Path.Combine(directory.Path, "missing.lua"), 0, "x"))
				);

				Assert.IsFalse(applied);
				Assert.AreEqual(before, pane.Editor.Text);
				Assert.Contains(static report => report.Contains("was not applied", StringComparison.Ordinal), reports);
			}
		);
	}

	[TestMethod]
	public async Task ApplyServerEditAsync_ForAFileOutsideTheSampleFolder_AnswersNotAppliedAndTouchesNothing()
	{
		using var directory = new TemporaryDirectory("sample-");
		using var outside = new TemporaryDirectory("sample-outside-");

		string outsideFile = System.IO.Path.Combine(outside.Path, "elsewhere.lua");

		System.IO.File.WriteAllText(outsideFile, "-- not the sample's\n");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				SamplePane pane = workbench.LuaPane;
				string before = pane.Editor.Text;

				var reports = new List<string>();

				workbench.StatusChanged += (_, e) => reports.Add(e.Message);

				bool applied = await Task.Run(() => workbench.ApplyServerEditAsync(Insert(outsideFile, 0, "-- written by a server\n")));

				// The edit reaches no file of the sample, and the status line says why.
				Assert.IsFalse(applied);
				Assert.AreEqual(before, pane.Editor.Text);
				Assert.AreEqual("-- not the sample's\n", System.IO.File.ReadAllText(outsideFile));
				Assert.Contains(static report => report.Contains("outside the sample folder", StringComparison.Ordinal), reports);
			}
		);
	}
}
