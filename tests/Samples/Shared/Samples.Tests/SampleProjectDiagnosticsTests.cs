using System.IO;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the project check of the sample, which marks the TODO and FIXME markers of the Markdown and text files of the folder.</summary>
[STATestClass]
public sealed class SampleProjectDiagnosticsTests
{
	private static DiagnosticsController DiagnosticsOf(SamplePane pane) =>
		pane.Session.Language.Diagnostics ?? throw new InvalidOperationException($"{pane.Title} has no diagnostics controller.");

	[TestMethod]
	public async Task ACheckedFile_GetsAHoverThatShowsItsDiagnostics_AlthoughNoHoverProviderAnswersIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane notes = await workbench.OpenAsync(Path.Combine(directory.Path, "README.md"));
				SamplePane code = await workbench.OpenAsync(Path.Combine(directory.Path, "Sample.cs"));

				Assert.IsNotNull(notes.Session.Language.Hover);
				Assert.IsNull(notes.Session.Features.Hover);
				Assert.IsNull(code.Session.Language.Hover);
			}
		);
	}

	[TestMethod]
	public async Task AMarkerTypedInAMarkdownEditor_IsMarkedThere_AndClearedWhenTheMarkerIsDeleted()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane notes = await workbench.OpenAsync(Path.Combine(directory.Path, "README.md"));
				DiagnosticsController diagnostics = DiagnosticsOf(notes);

				// The unsaved text counts, and the file on disk keeps the markers of the starter file.
				notes.Editor.Text = "# Notes\n\nTODO: write the rest\n";

				await SampleTestHost.WaitUntilAsync(
					() => diagnostics.Current.Count == 1 && diagnostics.Current[0].Range.Offset == 9,
					"The marker of the editor text was not marked."
				);

				Diagnostic marker = diagnostics.Current[0];

				Assert.AreEqual(new TextRange(9, 4), marker.Range);
				Assert.AreEqual(DiagnosticSeverity.Information, marker.Severity);
				Assert.AreEqual("TODO marker", marker.Message);

				notes.Editor.Text = "# Notes\n\nnothing left\n";

				await SampleTestHost.WaitUntilAsync(() => diagnostics.Current.Count == 0, "The deleted marker was not cleared.");
			}
		);
	}

	[TestMethod]
	public async Task AMarkerOfAFileThatNoEditorShows_ReachesTheEditorOpenedLater()
	{
		using var directory = new TemporaryDirectory("sample-");
		string path = Path.Combine(directory.Path, "todo.txt");
		File.WriteAllText(path, "buy milk\nFIXME the list\n");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane list = await workbench.OpenAsync(path);
				DiagnosticsController diagnostics = DiagnosticsOf(list);

				await SampleTestHost.WaitUntilAsync(() => diagnostics.Current.Count == 1, "The marker of the file on disk was not marked.");

				Assert.AreEqual(new TextRange(9, 5), diagnostics.Current[0].Range);
				Assert.AreEqual("FIXME marker", diagnostics.Current[0].Message);
			}
		);
	}

	[TestMethod]
	[DataRow("notes.md", true)]
	[DataRow("NOTES.MD", true)]
	[DataRow("notes.txt", true)]
	[DataRow("sample.lua", false)]
	[DataRow("settings.json", false)]
	[DataRow("Sample.cs", false)]
	public void Covers_NamesTheMarkdownAndTextFilesOnly(string name, bool expected) =>
		Assert.AreEqual(expected, SampleProjectDiagnostics.Covers(Path.Combine(Path.GetTempPath(), name)));
}
