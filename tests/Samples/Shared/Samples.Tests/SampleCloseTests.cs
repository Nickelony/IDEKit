using System.IO;
using System.Text;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers what the window asks and does with unsaved documents when it closes.</summary>
[STATestClass]
public sealed class SampleCloseTests
{
	private static async Task<SamplePane> OpenDirtyAsync(SampleWorkbench workbench, TemporaryDirectory directory, string name, string text)
	{
		string path = Path.Combine(directory.Path, name);
		await File.WriteAllTextAsync(path, "saved\n");
		SamplePane pane = await workbench.OpenAsync(path);

		pane.Editor.Text = text;

		return pane;
	}

	[TestMethod]
	public async Task ConfirmCloseAsync_WithNothingUnsaved_AsksNothing()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				int asked = 0;

				workbench.DecideSaveAsync = _ =>
				{
					asked++;
					return Task.FromResult(SampleSaveDecision.Cancel);
				};

				workbench.DecideSaveAllAsync = _ =>
				{
					asked++;
					return Task.FromResult(SampleSaveDecision.Cancel);
				};

				Assert.IsTrue(await workbench.ConfirmCloseAsync());
				Assert.AreEqual(0, asked);
			}
		);
	}

	[TestMethod]
	public async Task ConfirmCloseAsync_WithOneUnsavedDocument_AsksTheQuestionOfAClosingTab()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenDirtyAsync(workbench, directory, name: "one.txt", text: "typed\n");
				var asked = new List<SamplePane>();

				workbench.DecideSaveAsync = askedPane =>
				{
					asked.Add(askedPane);
					return Task.FromResult(SampleSaveDecision.Cancel);
				};

				Assert.IsFalse(await workbench.ConfirmCloseAsync());
				Assert.AreSame(pane, asked.Single());
				Assert.AreEqual("saved\n", await File.ReadAllTextAsync(pane.FilePath));
				Assert.IsTrue(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task ConfirmCloseAsync_WithSeveralUnsavedDocuments_AsksOnceForAllOfThem_AndCancelKeepsEveryDocument()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane first = await OpenDirtyAsync(workbench, directory, name: "one.txt", text: "typed one\n");
				SamplePane second = await OpenDirtyAsync(workbench, directory, name: "two.txt", text: "typed two\n");
				SamplePane split = (await workbench.SplitAsync(second))!;

				var asked = new List<IReadOnlyList<SamplePane>>();

				workbench.DecideSaveAllAsync = askedPanes =>
				{
					asked.Add(askedPanes);
					return Task.FromResult(SampleSaveDecision.Cancel);
				};

				Assert.IsFalse(await workbench.ConfirmCloseAsync());

				// The split of the second document is the same document, so it counts once.
				Assert.HasCount(1, asked);
				Assert.HasCount(2, asked[0]);
				Assert.AreEqual("saved\n", await File.ReadAllTextAsync(first.FilePath));
				Assert.AreEqual("saved\n", await File.ReadAllTextAsync(second.FilePath));
				Assert.IsNotNull(split);
			}
		);
	}

	[TestMethod]
	public async Task ConfirmCloseAsync_SaveWritesEveryUnsavedDocument_AndDiscardWritesNone()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane first = await OpenDirtyAsync(workbench, directory, name: "one.txt", text: "typed one\n");
				SamplePane second = await OpenDirtyAsync(workbench, directory, name: "two.txt", text: "typed two\n");

				workbench.DecideSaveAllAsync = _ => Task.FromResult(SampleSaveDecision.Discard);

				Assert.IsTrue(await workbench.ConfirmCloseAsync());
				Assert.AreEqual("saved\n", await File.ReadAllTextAsync(first.FilePath));
				Assert.AreEqual("saved\n", await File.ReadAllTextAsync(second.FilePath));

				workbench.DecideSaveAllAsync = _ => Task.FromResult(SampleSaveDecision.Save);

				Assert.IsTrue(await workbench.ConfirmCloseAsync());
				Assert.AreEqual("typed one\n", await File.ReadAllTextAsync(first.FilePath));
				Assert.AreEqual("typed two\n", await File.ReadAllTextAsync(second.FilePath));
				Assert.IsFalse(first.View.IsDirty);
				Assert.IsFalse(second.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task ConfirmCloseAsync_WhenASaveFails_KeepsTheWindowOpen()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				// The file is Windows-1252, so a character outside the code page cannot be written back.
				string latinPath = Path.Combine(directory.Path, "latin.txt");
				await File.WriteAllBytesAsync(latinPath, [0x63, 0x61, 0x66, 0xE9, 0x0A]);
				SamplePane latin = await workbench.OpenAsync(latinPath);
				latin.Editor.Text += "\u0159";
				await OpenDirtyAsync(workbench, directory, name: "two.txt", text: "typed two\n");

				workbench.DecideSaveAllAsync = _ => Task.FromResult(SampleSaveDecision.Save);

				Assert.IsFalse(await workbench.ConfirmCloseAsync());
				Assert.AreSequenceEqual(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x0A }, await File.ReadAllBytesAsync(latinPath));
				Assert.IsTrue(latin.View.IsDirty);
			}
		);
	}
}
