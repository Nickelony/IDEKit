using System.IO.Enumeration;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes one file-system watch pattern used by the workspace watcher.
/// </summary>
/// <param name="Filter">The <see cref="FileSystemWatcher"/> filter pattern.</param>
/// <param name="IncludeSubdirectories">Whether matching should recurse into subdirectories.</param>
public readonly record struct WorkspaceWatchSpecification(string Filter, bool IncludeSubdirectories)
{
	/// <summary>
	/// Validates one watch specification for the watcher and the snapshot tracker.
	/// </summary>
	/// <param name="specification">The specification to validate.</param>
	/// <param name="paramName">The parameter name used in the thrown exception.</param>
	/// <exception cref="ArgumentException">
	/// The filter is <see langword="null"/>, empty, or whitespace-only, is <c>.</c> or <c>..</c>, is rooted, or
	/// contains a directory separator, so it cannot address a file-name pattern under the workspace root.
	/// </exception>
	internal static void Validate(WorkspaceWatchSpecification specification, string paramName)
	{
		if (string.IsNullOrWhiteSpace(specification.Filter))
			throw new ArgumentException("A watch specification filter must not be null, empty, or whitespace-only.", paramName);

		if (specification.Filter is "." or "..")
			throw new ArgumentException("A watch specification filter must name a file pattern rather than a directory alias.", paramName);

		if (Path.IsPathRooted(specification.Filter)
			|| specification.Filter.Contains(Path.DirectorySeparatorChar)
			|| specification.Filter.Contains(Path.AltDirectorySeparatorChar))
		{
			throw new ArgumentException("A watch specification filter must be a file-name pattern without directory separators.", paramName);
		}
	}

	/// <summary>
	/// Reports whether a file path still matches this specification's filter.
	/// </summary>
	/// <param name="filePath">The file path to test against the filter.</param>
	/// <returns><see langword="true"/> when the file name matches the filter; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The match mirrors the runtime file-name matching the file-system watcher applies: an empty filter and
	/// <c>*.*</c> are normalized to <c>*</c>, and the simple wildcard grammar applies (<c>*</c> matches zero or
	/// more characters and <c>?</c> matches exactly one character), so the effective pattern is identical to the
	/// filter the <see cref="FileSystemWatcher"/> applied before it raised an event. Character casing follows the
	/// configured local-path comparison policy.
	/// </remarks>
	internal bool MatchesFileName(string filePath)
	{
		string fileName = Path.GetFileName(filePath);
		bool ignoreCase = !LanguageServerPaths.UsesCaseSensitiveLocalPaths;

		// FileSystemWatcher normalizes an empty filter and "*.*" to "*" before matching, so both spellings match
		// every name, including names without an extension.
		string filter = string.IsNullOrEmpty(Filter) || string.Equals(Filter, "*.*", StringComparison.Ordinal) ? "*" : Filter;

		return FileSystemName.MatchesSimpleExpression(filter.AsSpan(), fileName.AsSpan(), ignoreCase);
	}
}
