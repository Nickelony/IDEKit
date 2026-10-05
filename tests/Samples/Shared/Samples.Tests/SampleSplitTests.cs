using System.IO;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the second views of a document: the group they sit in while the document is renamed.</summary>
[STATestClass]
public sealed class SampleSplitTests
{
	[TestMethod]
	public async Task RenamingADocumentWithASplit_NeverTakesTheSplitOutOfTheSecondGroup()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane first = await workbench.OpenAsync(Path.Combine(directory.Path, "README.md"));
				SamplePane split = (await workbench.SplitAsync(first))!;

				var groups = new List<bool>();

				// The panes follow the new path one after the other, and a tab that leaves its group in between can come back blank, because a
				// toolkit may show the content of a tab in one parent only.
				workbench.TabsChanged += (_, _) => groups.Add(workbench.TabsBeside.Contains(split));

				await workbench.RenameEntryAsync(first.FilePath, "NOTES.md");

				for (int attempt = 0; attempt < 400 && split.FilePath != Path.Combine(directory.Path, "NOTES.md"); attempt++)
					await Task.Delay(25);

				Assert.AreEqual(Path.Combine(directory.Path, "NOTES.md"), split.FilePath);
				Assert.AreEqual(Path.Combine(directory.Path, "NOTES.md"), first.FilePath);
				Assert.IsNotEmpty(groups);
				Assert.IsTrue(groups.All(inSecondGroup => inSecondGroup), "The split left the second group during the rename.");
				Assert.Contains(split, workbench.TabsBeside);
			}
		);
	}
}
