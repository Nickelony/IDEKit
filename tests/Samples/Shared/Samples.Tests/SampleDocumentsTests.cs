using System.IO;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.Threading;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using Nickelony.IDEKit.AvalonEdit.Threading;

namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the document authority of the sample without a window: an edit to a file that no tab shows.</summary>
[STATestClass]
public sealed class SampleDocumentsTests
{
	[TestMethod]
	public async Task ApplyEdit_ToAFileThatNoTabShows_EditsAndSavesIt()
	{
		using var directory = new TemporaryDirectory("sample-");
		string path = Path.Combine(directory.Path, "closed.lua");
		File.WriteAllText(path, "local old = 1");

		await SampleTestHost.RunAsync(async () =>
		{
			var dispatcher = UiDispatcher.FromCurrent();
			await using var documents = new SampleDocuments($"{directory.Path}.Trash", dispatcher, action => dispatcher.Post(action));

			var edit = new WorkspaceEdit([
				new TextDocumentEdit(path, [new TextEdit(new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 9)), "new")]),
			]);

			Assert.AreEqual(1, await documents.ApplyEditAsync(edit, isShown: _ => false));
			Assert.AreEqual("local new = 1", File.ReadAllText(path));
		});
	}
}
