using System.Collections.Concurrent;
using Nickelony.IDEKit.Workspace;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Collects what the tasks of a soak observe, and reports the contract breaks it finds instead of throwing from the middle of a task.
/// </summary>
/// <remarks>
/// It checks three things across all tasks. Every result names an outcome the contract defines and none of the failure outcomes that a healthy
/// local file system cannot produce. One version of one document carries one content, so two edits never both succeed from the same state.
/// A replacement that succeeds moves the version up by exactly one.
/// </remarks>
internal sealed class RecordingSoakLog
{
	// The outcomes that report a file-system failure or a cancellation. The soak runs on a local temporary directory and cancels nothing,
	// so none of them has a cause.
	private static readonly HashSet<string> s_unexpectedOutcomes = new(StringComparer.Ordinal)
	{
		"Canceled",
		"DeleteFailed",
		"InvalidPath",
		"IsDirectory",
		"LoadFailed",
		"MoveFailed",
		"MoveStateUnknown",
		"NotFound",
		"ReadFailed",
		"ReplacementStateUnknown",
		"WriteFailed",
	};

	private readonly ConcurrentDictionary<(WorkspaceDocumentKey Key, long Version), string> _contents = new();
	private readonly ConcurrentDictionary<(WorkspaceDocumentKey Key, long BaseVersion), int> _acceptedReplacements = new();
	private readonly ConcurrentDictionary<string, int> _outcomes = new(StringComparer.Ordinal);
	private readonly ConcurrentQueue<string> _violations = new();

	/// <summary>The number of results seen, by operation and outcome, for the test output.</summary>
	internal string Summary =>
		string.Join(", ", _outcomes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));

	/// <summary>Records the outcome of one result and checks that the contract names it and that a local file system could produce it.</summary>
	/// <param name="operation">The operation that produced the result.</param>
	/// <param name="outcome">The outcome the result carries.</param>
	/// <param name="failure">The failure the result carries, which a break reports.</param>
	internal void Record<TOutcome>(string operation, TOutcome outcome, WorkspaceOperationFailure? failure = null)
		where TOutcome : struct, Enum
	{
		_outcomes.AddOrUpdate($"{operation}.{outcome}", 1, static (_, count) => count + 1);

		if (!Enum.IsDefined(outcome))
			Violate($"{operation} returned the undefined outcome {outcome}. {failure?.Message}");
		else if (s_unexpectedOutcomes.Contains(outcome.ToString()))
			Violate($"{operation} returned {outcome}, which a local temporary directory gives no cause for. {failure?.Message}");
	}

	/// <summary>Records a snapshot that a result carried and checks that its version has one content.</summary>
	/// <param name="snapshot">The snapshot, or <see langword="null"/> when the result carried none.</param>
	internal void Observe(WorkspaceDocumentSnapshot? snapshot)
	{
		if (snapshot is null)
			return;

		string content = snapshot.Content;
		string known = _contents.GetOrAdd((snapshot.DocumentKey, snapshot.Version), content);

		if (!string.Equals(known, content, StringComparison.Ordinal))
			Violate($"Version {snapshot.Version} of {snapshot.DisplayPath} has two contents: '{known}' and '{content}'.");
	}

	/// <summary>
	/// Records a replacement and checks that it moved the version by one and that no other replacement succeeded from the same version.
	/// </summary>
	/// <param name="requested">The identity the replacement was requested against.</param>
	/// <param name="result">The result of the replacement.</param>
	internal void RecordReplacement(WorkspaceDocumentRequestIdentity requested, WorkspaceDocumentMutationResult result)
	{
		Record("Replace", result.Outcome);
		Observe(result.Snapshot);

		if (result.Outcome != WorkspaceDocumentMutationOutcome.Changed || result.Snapshot is null)
			return;

		if (result.Snapshot.Version != requested.Version + 1)
			Violate($"A replacement from version {requested.Version} of {result.Snapshot.DisplayPath} produced version {result.Snapshot.Version}.");

		if (_acceptedReplacements.AddOrUpdate((requested.DocumentKey, requested.Version), 1, static (_, count) => count + 1) > 1)
			Violate($"Two replacements of {result.Snapshot.DisplayPath} both succeeded from version {requested.Version}, so one edit is lost.");
	}

	/// <summary>
	/// Checks the documents the store tracks under a directory once every task has finished: each path and each document has one entry,
	/// the entry is reachable by its path, a persisted document has its file, an unpersisted one has none, and a clean document holds the text
	/// of its file.
	/// </summary>
	/// <param name="documents">The store, or the reader the coordinator exposes.</param>
	/// <param name="directory">The directory every soak document sits under.</param>
	internal void CheckTrackedDocuments(IWorkspaceDocumentReader documents, string directory)
	{
		IReadOnlyList<WorkspaceDocumentSnapshot> tracked = documents.GetSnapshotsUnderDirectory(directory);

		foreach (
			IGrouping<string, WorkspaceDocumentSnapshot> group in tracked.GroupBy(snapshot => snapshot.DocumentId, StringComparer.OrdinalIgnoreCase)
		)
		{
			if (group.Count() > 1)
				Violate($"{group.Key} is tracked {group.Count()} times.");
		}

		foreach (IGrouping<WorkspaceDocumentKey, WorkspaceDocumentSnapshot> group in tracked.GroupBy(snapshot => snapshot.DocumentKey))
		{
			if (group.Count() > 1)
				Violate($"One document instance is tracked under {group.Count()} paths, such as {group.First().DisplayPath}.");
		}

		foreach (WorkspaceDocumentSnapshot snapshot in tracked)
		{
			Observe(snapshot);

			if (!documents.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? byPath) || byPath.DocumentKey != snapshot.DocumentKey)
			{
				Violate($"{snapshot.DisplayPath} is listed under its directory but not reachable by its path.");
				continue;
			}

			bool fileExists = File.Exists(snapshot.DocumentId);

			if (snapshot.ExistsOnDisk != fileExists)
			{
				Violate(
					$"{snapshot.DisplayPath} is tracked as {(snapshot.ExistsOnDisk ? "persisted" : "unpersisted")} but its file {(fileExists ? "exists" : "is missing")}."
				);
			}
			else if (fileExists && !snapshot.IsDirty && File.ReadAllText(snapshot.DocumentId) != snapshot.Content)
			{
				Violate($"{snapshot.DisplayPath} is clean but differs from its file.");
			}
		}
	}

	/// <summary>Records a break of the contract that a task found.</summary>
	/// <param name="message">What broke.</param>
	internal void Violate(string message) => _violations.Enqueue(message);

	/// <summary>Fails the test with every recorded break, the seed, and the outcome counts.</summary>
	/// <param name="seed">The seed of the run.</param>
	internal void AssertNoViolations(int seed)
	{
		string[] violations = [.. _violations.Distinct(StringComparer.Ordinal).Take(25)];

		Assert.IsEmpty(
			violations,
			$"The soak broke the contract. Seed {seed}.{Environment.NewLine}{string.Join(Environment.NewLine, violations)}{Environment.NewLine}Outcomes: {Summary}"
		);
	}
}
