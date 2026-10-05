namespace Nickelony.IDEKit.FileWatching.Tests;

[TestClass]
public sealed class FileSnapshotDifferTests
{
	private static readonly FileSnapshotEntry s_entry = new(IsDirectory: false, LastWriteUtcTicks: 1, Length: 1, ContentFingerprint: 1);

	[TestMethod]
	public void Build_ReportsCreatedChangedAndDeletedPaths()
	{
		string changed = TestPaths.Script("changed.ext");
		string created = TestPaths.Script("created.ext");
		string deleted = TestPaths.Script("deleted.ext");
		string unchanged = TestPaths.Script("unchanged.ext");

		var previous = new Dictionary<string, FileSnapshotEntry>
		{
			[changed] = s_entry,
			[deleted] = s_entry,
			[unchanged] = s_entry,
		};

		var current = new Dictionary<string, FileSnapshotEntry>
		{
			[changed] = s_entry with { LastWriteUtcTicks = 2 },
			[created] = s_entry,
			[unchanged] = s_entry,
		};

		FileChangeBatch batch = FileSnapshotDiffer.Build(
			previousSnapshot: previous,
			currentSnapshot: current,
			pathComparison: TestPaths.PathComparison,
			isPathMissing: static _ => true
		);

		Assert.AreSequenceEqual(
			[
				new FileChange(changed, FileChangeKind.Changed),
				new FileChange(created, FileChangeKind.Created),
				new FileChange(deleted, FileChangeKind.Deleted),
			],

			[.. batch.Entries]
		);
	}

	[TestMethod]
	public void Build_ForAPathTheCurrentSnapshotLacksButTheProbeDoesNotCallMissing_ReportsNothing()
	{
		string skipped = TestPaths.Script("skipped.ext");

		var previous = new Dictionary<string, FileSnapshotEntry> { [skipped] = s_entry };

		FileChangeBatch batch = FileSnapshotDiffer.Build(
			previousSnapshot: previous,
			currentSnapshot: new Dictionary<string, FileSnapshotEntry>(),
			pathComparison: TestPaths.PathComparison,
			isPathMissing: static _ => false
		);

		Assert.AreEqual(0, batch.Count);
	}

	[TestMethod]
	public void Build_AsksTheProbeOnlyForPathsThatDisappeared()
	{
		string kept = TestPaths.Script("kept.ext");
		string gone = TestPaths.Script("gone.ext");

		var asked = new List<string>();

		var previous = new Dictionary<string, FileSnapshotEntry> { [kept] = s_entry, [gone] = s_entry };
		var current = new Dictionary<string, FileSnapshotEntry> { [kept] = s_entry };

		_ = FileSnapshotDiffer.Build(
			previousSnapshot: previous,
			currentSnapshot: current,
			pathComparison: TestPaths.PathComparison,

			isPathMissing: path =>
			{
				asked.Add(path);
				return true;
			}
		);

		Assert.AreSequenceEqual([gone], [.. asked]);
	}

	[TestMethod]
	[DataRow(false, "B.ext", "a.ext")]
	[DataRow(true, "a.ext", "B.ext")]
	public void Build_OrdersTheChangesByThePathComparison(bool ignoreCase, string expectedFirstName, string expectedSecondName)
	{
		LocalPathComparisonPolicy pathComparison = ignoreCase ? LocalPathComparisonPolicy.CaseInsensitive : LocalPathComparisonPolicy.CaseSensitive;

		var current = new Dictionary<string, FileSnapshotEntry> { [TestPaths.Script("B.ext")] = s_entry, [TestPaths.Script("a.ext")] = s_entry };

		FileChangeBatch batch = FileSnapshotDiffer.Build(
			previousSnapshot: new Dictionary<string, FileSnapshotEntry>(),
			currentSnapshot: current,
			pathComparison,
			isPathMissing: static _ => true
		);

		Assert.AreEqual(2, batch.Count);
		Assert.AreEqual(TestPaths.Script(expectedFirstName), batch.Entries[0].Path);
		Assert.AreEqual(TestPaths.Script(expectedSecondName), batch.Entries[1].Path);
	}

	[TestMethod]
	public void Build_ReportsAnEntryThatDiffersOnlyByContentFingerprintAsChanged()
	{
		string path = TestPaths.Script("fingerprint.ext");

		FileChangeBatch batch = FileSnapshotDiffer.Build(
			previousSnapshot: new Dictionary<string, FileSnapshotEntry> { [path] = s_entry },
			currentSnapshot: new Dictionary<string, FileSnapshotEntry> { [path] = s_entry with { ContentFingerprint = 2 } },
			pathComparison: TestPaths.PathComparison,
			isPathMissing: static _ => true
		);

		Assert.AreSequenceEqual([new FileChange(path, FileChangeKind.Changed)], [.. batch.Entries]);
	}
}
