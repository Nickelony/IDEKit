using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.IDEKit.FileWatching.Tests;

[TestClass]
public sealed class FileSnapshotWalkerTests
{
	private static FileSnapshotWalker CreateWalker(string root, params FileWatchSpecification[] specifications)
	{
		return new(
			workspaceRootDirectoryPath: LocalPath.Normalize(root),
			watchSpecifications: specifications,
			pathComparison: TestPaths.PathComparison,
			logger: NullLogger.Instance
		);
	}

	[TestMethod]
	public void Walk_ForAMissingRoot_ReturnsAnEmptySnapshot()
	{
		string missingRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotWalker_" + Guid.NewGuid().ToString("N"));
		FileSnapshotWalker walker = CreateWalker(missingRoot, new FileWatchSpecification("*.txt", IncludeSubdirectories: true));

		Assert.IsFalse(walker.RootExists());
		Assert.IsEmpty(walker.Walk());
	}

	[TestMethod]
	public void Walk_ReadsTheFilesTheSpecificationsSelectAndNothingElse()
	{
		using var workspace = new TemporaryDirectory("WorkspaceSnapshotWalker_");

		string nested = Path.Combine(workspace.Path, "Nested");
		Directory.CreateDirectory(nested);

		File.WriteAllText(Path.Combine(workspace.Path, "root.txt"), "one");
		File.WriteAllText(Path.Combine(nested, "nested.txt"), "two");
		File.WriteAllText(Path.Combine(workspace.Path, "other.md"), "three");

		FileSnapshotWalker walker = CreateWalker(workspace.Path, new FileWatchSpecification("*.txt", IncludeSubdirectories: true));

		Dictionary<string, FileSnapshotEntry> snapshot = walker.Walk();

		Assert.HasCount(2, snapshot);
		Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(Path.Combine(workspace.Path, "root.txt"))));
		Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(Path.Combine(nested, "nested.txt"))));
	}

	[TestMethod]
	public void Walk_ForANonRecursiveSpecification_SkipsSubdirectories()
	{
		using var workspace = new TemporaryDirectory("WorkspaceSnapshotWalker_");

		string nested = Path.Combine(workspace.Path, "Nested");
		Directory.CreateDirectory(nested);

		File.WriteAllText(Path.Combine(workspace.Path, "root.txt"), "one");
		File.WriteAllText(Path.Combine(nested, "nested.txt"), "two");

		FileSnapshotWalker walker = CreateWalker(workspace.Path, new FileWatchSpecification("*.txt", IncludeSubdirectories: false));

		Dictionary<string, FileSnapshotEntry> snapshot = walker.Walk();

		Assert.AreEqual(LocalPath.Normalize(Path.Combine(workspace.Path, "root.txt")), Assert.ContainsSingle(snapshot.Keys));
	}

	[TestMethod]
	public void TryCreateEntry_FingerprintsTheContentOfAFile()
	{
		using var workspace = new TemporaryDirectory("WorkspaceSnapshotWalker_");

		string first = LocalPath.Normalize(Path.Combine(workspace.Path, "first.txt"));
		string second = LocalPath.Normalize(Path.Combine(workspace.Path, "second.txt"));

		File.WriteAllText(first, "return 1");
		File.WriteAllText(second, "return 2");

		Assert.IsTrue(FileSnapshotWalker.TryCreateEntry(first, out FileSnapshotEntry firstEntry));
		Assert.IsTrue(FileSnapshotWalker.TryCreateEntry(second, out FileSnapshotEntry secondEntry));

		Assert.IsFalse(firstEntry.IsDirectory);
		Assert.AreEqual(firstEntry.Length, secondEntry.Length);
		Assert.AreNotEqual(firstEntry.ContentFingerprint, secondEntry.ContentFingerprint);
	}

	[TestMethod]
	public void TryCreateEntry_ForAMissingPath_ReturnsFalse() =>
		Assert.IsFalse(FileSnapshotWalker.TryCreateEntry(LocalPath.Normalize(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), out _));

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void TryCreateEntry_ForADirectoryJunction_ReturnsFalse()
	{
		using var workspace = new TemporaryDirectory("WorkspaceSnapshotWalker_");

		string target = Path.Combine(workspace.Path, "target");
		string junction = Path.Combine(workspace.Path, "junction");

		Directory.CreateDirectory(target);

		// A junction needs no privilege, unlike a symbolic link.
		using Process mklink =
			Process.Start(
				new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
				{
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
				}
			) ?? throw new InvalidOperationException("mklink did not start.");

		mklink.WaitForExit();

		Assert.AreEqual(0, mklink.ExitCode);
		Assert.IsTrue(FileSnapshotWalker.TryCreateEntry(LocalPath.Normalize(target), out _));
		Assert.IsFalse(FileSnapshotWalker.TryCreateEntry(LocalPath.Normalize(junction), out _), "A reparse point is never tracked.");

		Directory.Delete(junction);
	}

	[TestMethod]
	public void IsPathMissing_DistinguishesAnExistingPathFromAMissingOne()
	{
		using var workspace = new TemporaryDirectory("WorkspaceSnapshotWalker_");

		string existing = LocalPath.Normalize(Path.Combine(workspace.Path, "existing.txt"));
		File.WriteAllText(existing, "return 1");

		Assert.IsFalse(FileSnapshotWalker.IsPathMissing(existing));
		Assert.IsTrue(FileSnapshotWalker.IsPathMissing(LocalPath.Normalize(Path.Combine(workspace.Path, "missing.txt"))));
	}
}
