using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace Nickelony.IDEKit.FileWatching;

/// <summary>Reads the files that the watch specifications select under one workspace root into snapshot entries.</summary>
/// <remarks>
/// <para>
/// The walk follows the watch specifications and includes hidden and system files because the file-system watcher observes
/// them as well; reparse points (symbolic links and junctions) are excluded because neither the walk nor the watcher follows them consistently.
/// Enumeration problems such as a directory that disappears mid-walk are contained and reported at debug level, so a walk does not throw
/// for file-system races.
/// </para>
/// </remarks>
internal sealed class FileSnapshotWalker
{
	/// <summary>
	/// The number of leading and trailing bytes sampled into the content fingerprint of a file that is too large to hash in full, so the fingerprint
	/// cost stays constant instead of reading a multi-megabyte file on the dispatch path.
	/// </summary>
	private const int FingerprintSampleLength = 8 * 1024;

	// Enumeration options for a literal (wildcard-free) filter that recurses: the filter is not a wildcard, so the
	// walk enumerates every file and the specification's own matcher selects the copies. The options mirror the
	// per-specification options so a recursive literal walk and a recursive wildcard walk skip the same
	// inaccessible and reparse-point entries.
	private static readonly EnumerationOptions s_recursiveLiteralEnumerationOptions = new()
	{
		IgnoreInaccessible = true,
		RecurseSubdirectories = true,
		ReturnSpecialDirectories = false,
		AttributesToSkip = FileAttributes.ReparsePoint,
	};

	private readonly ILogger _logger;
	private readonly string _workspaceRootDirectoryPath;
	private readonly ReadOnlyCollection<FileWatchSpecification> _watchSpecifications;
	private readonly LocalPathComparisonPolicy _pathComparison;

	// Enumeration options for the wildcard specifications, precomputed in the constructor. EnumerationOptions is
	// immutable once configured and Directory.EnumerateFiles does not mutate it, so a walk must not rebuild a
	// fresh instance per specification per walk; a non-wildcard specification has no options because its filter
	// names a single path instead of a search pattern.
	private readonly EnumerationOptions?[] _enumerationOptions;

	/// <param name="workspaceRootDirectoryPath">The workspace root directory whose files are walked.</param>
	/// <param name="watchSpecifications">The validated file patterns that select the files. The list is copied on assignment.</param>
	/// <param name="pathComparison">The comparison that decides which paths are one file.</param>
	/// <param name="logger">The logger instance.</param>
	internal FileSnapshotWalker(
		string workspaceRootDirectoryPath,
		IReadOnlyList<FileWatchSpecification> watchSpecifications,
		LocalPathComparisonPolicy pathComparison,
		ILogger logger
	)
	{
		_workspaceRootDirectoryPath = workspaceRootDirectoryPath;
		_pathComparison = pathComparison;
		_logger = logger;
		_watchSpecifications = Array.AsReadOnly([.. watchSpecifications]);
		_enumerationOptions = new EnumerationOptions?[_watchSpecifications.Count];

		for (int i = 0; i < _watchSpecifications.Count; i++)
		{
			FileWatchSpecification specification = _watchSpecifications[i];

			if (specification.Filter.IndexOfAny(['*', '?']) < 0)
				continue;

			_enumerationOptions[i] = new EnumerationOptions
			{
				IgnoreInaccessible = true,
				RecurseSubdirectories = specification.IncludeSubdirectories,
				ReturnSpecialDirectories = false,

				// Hidden and system files are captured because the file-system watcher observes them; reparse points
				// are skipped so the walk cannot escape the workspace through symbolic links or junctions.
				AttributesToSkip = FileAttributes.ReparsePoint,

				// The walk must apply the same simple wildcard grammar the watch specification matcher uses, so the
				// walked scope and the forwarded-event scope cannot disagree on exotic patterns.
				MatchType = MatchType.Simple,
			};
		}
	}

	/// <summary>Determines whether the workspace root directory exists.</summary>
	/// <returns><see langword="true"/> when the root exists; otherwise, <see langword="false"/>.</returns>
	internal bool RootExists() => Directory.Exists(_workspaceRootDirectoryPath);

	/// <summary>Walks the workspace root and reads every file that a watch specification selects.</summary>
	/// <returns>A new snapshot, keyed by normalized path; empty when the root does not exist.</returns>
	internal Dictionary<string, FileSnapshotEntry> Walk()
	{
		var snapshot = new Dictionary<string, FileSnapshotEntry>(_pathComparison.Comparer);

		if (!RootExists())
			return snapshot;

		for (int i = 0; i < _watchSpecifications.Count; i++)
			WalkSpecification(snapshot, _watchSpecifications[i], _enumerationOptions[i]);

		return snapshot;
	}

	/// <summary>Creates one snapshot entry for a normalized path.</summary>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <param name="entry">Receives the snapshot entry when the path exists.</param>
	/// <param name="logger">The logger used for unexpected read failures.</param>
	/// <returns><see langword="true"/> when an entry was read; otherwise, <see langword="false"/>.</returns>
	internal static bool TryCreateEntry(string normalizedPath, out FileSnapshotEntry entry, ILogger? logger = null)
	{
		entry = default;

		try
		{
			FileAttributes attributes = File.GetAttributes(normalizedPath);

			// Reparse points (symbolic links and junctions) are excluded from the tracked snapshot on every
			// read path, matching the walk's AttributesToSkip filter: the watcher does not follow them
			// consistently, so a tracked entry could never agree with a later full walk.
			if ((attributes & FileAttributes.ReparsePoint) != 0)
				return false;

			if ((attributes & FileAttributes.Directory) != 0)
			{
				var directoryInfo = new DirectoryInfo(normalizedPath);

				entry = new FileSnapshotEntry(
					IsDirectory: true,
					LastWriteUtcTicks: directoryInfo.LastWriteTimeUtc.Ticks,
					Length: 0,
					ContentFingerprint: 0
				);

				return true;
			}

			var fileInfo = new FileInfo(normalizedPath);

			entry = new FileSnapshotEntry(
				IsDirectory: false,
				LastWriteUtcTicks: fileInfo.LastWriteTimeUtc.Ticks,
				Length: fileInfo.Length,
				ContentFingerprint: ComputeFileContentFingerprint(normalizedPath)
			);

			return true;
		}
		catch (Exception exception)
		{
			logger?.LogDebug(exception, "Failed to capture a workspace snapshot entry for '{Path}'.", normalizedPath);
		}

		return false;
	}

	/// <summary>Determines whether a normalized path is missing from the file system.</summary>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <param name="isMissing">Receives whether the path does not exist.</param>
	/// <param name="logger">The logger used for unexpected access failures.</param>
	/// <returns><see langword="true"/> when the missing state was resolved; otherwise, <see langword="false"/>.</returns>
	internal static bool TryDeterminePathMissing(string normalizedPath, out bool isMissing, ILogger? logger = null)
	{
		isMissing = false;

		try
		{
			_ = File.GetAttributes(normalizedPath);
			return true;
		}
		catch (DirectoryNotFoundException)
		{
			isMissing = true;
			return true;
		}
		catch (FileNotFoundException)
		{
			isMissing = true;
			return true;
		}
		catch (Exception exception)
		{
			logger?.LogDebug(exception, "Failed to determine whether the workspace path '{Path}' is missing.", normalizedPath);
			return false;
		}
	}

	/// <summary>Determines whether a normalized path is provably missing from the file system.</summary>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <returns>
	/// <see langword="true"/> when the path does not exist; <see langword="false"/> when it exists or its state could not be resolved.
	/// </returns>
	internal static bool IsPathMissing(string normalizedPath) => TryDeterminePathMissing(normalizedPath, out bool isMissing) && isMissing;

	private void WalkSpecification(
		Dictionary<string, FileSnapshotEntry> snapshot,
		FileWatchSpecification watchSpecification,
		EnumerationOptions? enumerationOptions
	)
	{
		try
		{
			if (enumerationOptions is null)
			{
				// A literal filter that does not recurse addresses exactly one root-level file.
				if (!watchSpecification.IncludeSubdirectories)
				{
					AddSnapshotPath(snapshot, Path.Combine(_workspaceRootDirectoryPath, watchSpecification.Filter));
					return;
				}

				// A literal filter that recurses makes the watcher observe a copy in any subdirectory, so the walk
				// must record every matching copy too. Otherwise a recovery snapshot would omit them and a delta would
				// report the subdirectory copies as deleted.
				foreach (string filePath in Directory.EnumerateFiles(_workspaceRootDirectoryPath, "*", s_recursiveLiteralEnumerationOptions))
				{
					if (watchSpecification.MatchesFileName(filePath, _pathComparison))
						AddSnapshotPath(snapshot, filePath);
				}

				return;
			}

			foreach (string filePath in Directory.EnumerateFiles(_workspaceRootDirectoryPath, watchSpecification.Filter, enumerationOptions))
				AddSnapshotPath(snapshot, filePath);
		}
		catch (Exception exception)
		{
			// Enumeration races (a directory removed mid-walk) and access failures are contained here so watcher
			// start and recovery never fail because of file-system churn; the next walk picks the state up.
			_logger.LogDebug(
				exception,
				"Failed to enumerate workspace files for filter '{Filter}' under '{Workspace}'.",
				watchSpecification.Filter,
				_workspaceRootDirectoryPath
			);
		}
	}

	private void AddSnapshotPath(Dictionary<string, FileSnapshotEntry> snapshot, string path)
	{
		if (!LocalPath.TryNormalize(path, out string normalizedPath))
			return;

		if (TryCreateEntry(normalizedPath, out FileSnapshotEntry entry, _logger))
			snapshot[normalizedPath] = entry;
	}

	/// <summary>Computes a compact content fingerprint for one file.</summary>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <returns>The first eight bytes of the SHA-256 hash over the hashed sample.</returns>
	/// <remarks>
	/// The fingerprint disambiguates entries whose timestamp and length are equal, for example when a file is rewritten
	/// twice within one file-system timestamp tick. It is computed for every created or changed path, because a tracked entry must stay comparable
	/// with a fresh full walk of the same content; skipping the read would make them disagree and report a spurious change. A file up to twice
	/// <see cref="FingerprintSampleLength"/> is hashed in full; a larger file is hashed from a bounded head and tail sample, so the cost
	/// stays constant instead of reading a multi-megabyte file on the dispatch path. The bounded sample can miss a same-length rewrite that changes
	/// only the middle of a large file within one file-system timestamp tick, which is an accepted trade-off for the constant cost.
	/// </remarks>
	private static ulong ComputeFileContentFingerprint(string normalizedPath)
	{
		using FileStream stream = File.OpenRead(normalizedPath);

		byte[] hash = stream.Length <= FingerprintSampleLength * 2 ? SHA256.HashData(stream) : HashHeadAndTailFingerprint(stream);

		return BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, sizeof(ulong)));
	}

	/// <summary>Hashes a bounded head and tail sample of a file that is too large to hash in full.</summary>
	/// <param name="stream">The readable file stream, positioned anywhere.</param>
	/// <returns>The SHA-256 hash of the concatenated head and tail samples.</returns>
	private static byte[] HashHeadAndTailFingerprint(FileStream stream)
	{
		byte[] sample = new byte[FingerprintSampleLength * 2];

		stream.ReadExactly(sample, 0, FingerprintSampleLength);
		stream.Seek(-FingerprintSampleLength, SeekOrigin.End);
		stream.ReadExactly(sample, FingerprintSampleLength, FingerprintSampleLength);

		return SHA256.HashData(sample);
	}
}
