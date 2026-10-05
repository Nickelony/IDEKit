using System.IO.Enumeration;
using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>Decides which files and folders below a workspace root stay out of the file list, by the globs of the options.</summary>
/// <remarks>
/// <para>
/// A glob without a slash is matched against the name of an entry at any depth.
/// A glob with a slash is matched segment by segment against the path of an entry below the root, written with slashes: a segment matches
/// one segment of the path with the simple wildcards, and the segment <c>**</c> matches zero or more segments. A folder that matches excludes
/// everything below it: the walk does not enter it, and <see cref="ExcludesPath"/> tests each folder of a path.
/// </para>
/// <para>
/// The filter is immutable, so any thread can call it.
/// </para>
/// </remarks>
internal sealed class WorkspaceFileFilter
{
	private const string AnyDepthSegment = "**";

	private readonly string[] _nameGlobs;
	private readonly string[][] _pathGlobs;
	private readonly bool _ignoreCase;

	/// <param name="globs">The globs of the options.</param>
	/// <param name="pathComparison">The comparison that decides whether casing matters.</param>
	internal WorkspaceFileFilter(IReadOnlyList<string> globs, LocalPathComparisonPolicy pathComparison)
	{
		List<string> names = [];
		List<string[]> paths = [];

		foreach (string glob in globs)
		{
			string[] segments = glob.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

			if (segments.Length == 0)
				continue;

			if (segments.Length == 1 && segments[0] != AnyDepthSegment)
				names.Add(segments[0]);
			else
				paths.Add(CollapseAnyDepth(segments));
		}

		_nameGlobs = [.. names];
		_pathGlobs = [.. paths];
		_ignoreCase = pathComparison.IgnoreCase;
	}

	/// <summary>Tells whether one file or folder is excluded, without looking at the folders above it.</summary>
	/// <param name="name">The name of the entry.</param>
	/// <param name="relativePath">The path of the entry below the root, written with slashes.</param>
	internal bool Excludes(string name, string relativePath)
	{
		foreach (string glob in _nameGlobs)
		{
			if (FileSystemName.MatchesSimpleExpression(glob, name, _ignoreCase))
				return true;
		}

		foreach (string[] glob in _pathGlobs)
		{
			if (MatchesFrom(glob, 0, relativePath))
				return true;
		}

		return false;
	}

	/// <summary>Tells whether a path is excluded itself or lies below an excluded folder.</summary>
	/// <param name="relativePath">The path below the root, written with slashes.</param>
	internal bool ExcludesPath(string relativePath)
	{
		if (_nameGlobs.Length == 0 && _pathGlobs.Length == 0)
			return false;

		int start = 0;

		while (start < relativePath.Length)
		{
			int separator = relativePath.IndexOf('/', start);
			int end = separator < 0 ? relativePath.Length : separator;

			if (Excludes(relativePath[start..end], relativePath[..end]))
				return true;

			start = end + 1;
		}

		return false;
	}

	// Two adjacent any-depth segments mean the same as one.
	private static string[] CollapseAnyDepth(string[] segments)
	{
		List<string> collapsed = new(segments.Length);

		foreach (string segment in segments)
		{
			if (segment == AnyDepthSegment && collapsed.Count > 0 && collapsed[^1] == AnyDepthSegment)
				continue;

			collapsed.Add(segment);
		}

		return [.. collapsed];
	}

	// Matches the segments of the glob from one position against the rest of the path, which has no segments left when it is empty.
	private bool MatchesFrom(string[] glob, int globIndex, ReadOnlySpan<char> path)
	{
		if (globIndex == glob.Length)
			return path.IsEmpty;

		if (glob[globIndex] == AnyDepthSegment)
		{
			if (globIndex == glob.Length - 1)
				return true;

			ReadOnlySpan<char> rest = path;

			while (true)
			{
				if (MatchesFrom(glob, globIndex + 1, rest))
					return true;

				if (rest.IsEmpty)
					return false;

				rest = AfterFirstSegment(rest);
			}
		}

		if (path.IsEmpty)
			return false;

		int separator = path.IndexOf('/');
		ReadOnlySpan<char> segment = separator < 0 ? path : path[..separator];

		return FileSystemName.MatchesSimpleExpression(glob[globIndex], segment, _ignoreCase)
			&& MatchesFrom(glob, globIndex + 1, AfterFirstSegment(path));
	}

	private static ReadOnlySpan<char> AfterFirstSegment(ReadOnlySpan<char> path)
	{
		int separator = path.IndexOf('/');
		return separator < 0 ? [] : path[(separator + 1)..];
	}
}
