using System.IO;
using Nickelony.IDEKit.FileWatching;
#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.Threading;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using Nickelony.IDEKit.AvalonEdit.Threading;

namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers how the reloader of the sample follows a folder over a watcher error.</summary>
[STATestClass]
public sealed class SampleFileReloaderTests
{
	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	public async Task WatchFolder_AfterAWatcherError_ReloadsALaterChange()
	{
		using var directory = new TemporaryDirectory("sample-");
		string path = Path.Combine(directory.Path, "watched.lua");
		File.WriteAllText(path, "one");

		FileWatcher? firstWatcher = null;
		Action<IFileWatcher, Exception?>? reportFailure = null;

		FileWatcher CreateWatcher(string folder, Func<FileChangeBatch, CancellationToken, Task> dispatchAsync, Action<IFileWatcher, Exception?> onFailed)
		{
			var watcher = new FileWatcher(
				folder,
				dispatchAsync,
				watchSpecifications: [new FileWatchSpecification("*", IncludeSubdirectories: true)],
				onWatcherFailed: onFailed
			);

			if (firstWatcher is null)
			{
				firstWatcher = watcher;
				reportFailure = onFailed;
			}

			return watcher;
		}

		await SampleTestHost.RunAsync(async () =>
		{
			var dispatcher = UiDispatcher.FromCurrent();
			var texts = new List<string>();

			await using var documents = new SampleDocuments(
				$"{directory.Path}.Trash",
				dispatcher,
				action => dispatcher.Post(action),
				watcherFactory: CreateWatcher
			);

			documents.DiskTextApplied += (_, e) => texts.Add(e.Text);

			await documents.OpenAsync(path);
			await documents.Reloader.WatchFolderAsync(directory.Path);

			await File.WriteAllTextAsync(path, "two");
			await SampleTestHost.WaitUntilAsync(() => texts.Contains("two"), "The change before the error was not reloaded.");

			Assert.IsNotNull(firstWatcher);
			Assert.IsNotNull(reportFailure);
			reportFailure(firstWatcher, new IOException("Internal buffer overflow."));

			await File.WriteAllTextAsync(path, "three");
			await SampleTestHost.WaitUntilAsync(() => texts.Contains("three"), "The change after the error was not reloaded.");
		});
	}
}
