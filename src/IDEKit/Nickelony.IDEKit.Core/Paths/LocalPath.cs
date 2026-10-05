using System.Runtime.CompilerServices;
using Nickelony.Infrastructure;

namespace Nickelony.IDEKit.Core.Paths;

/// <summary>Normalizes local file-system paths into the one spelling used as document identity.</summary>
/// <remarks>
/// A path is normalized for identity, so it must be fully qualified: a relative path would resolve against the process's current directory
/// and bind a document's identity to ambient process state. A normalized path is a full path with unified separators and no trailing separator.
/// It preserves the input casing and is not Unicode-normalized, so it is a display and protocol value rather than a case-insensitive key:
/// compare normalized paths with the <see cref="LocalPathComparisonPolicy"/> of the host. Language-server clients and providers apply the same rules
/// through the same source file, so a path normalizes to the same value in every package of the family.
/// </remarks>
public static class LocalPath
{
	/// <summary>Normalizes a fully qualified local path.</summary>
	/// <param name="path">The fully qualified path to normalize.</param>
	/// <returns>The normalized path.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="path"/> is empty, whitespace-only, relative, or invalid on the current platform.
	/// </exception>
	/// <exception cref="PathTooLongException">The resolved path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">The path contains a colon that is not part of a volume identifier.</exception>
	public static string Normalize(string path) => LocalPathRules.Normalize(path);

	/// <summary>Tries to normalize a local path without throwing for invalid input.</summary>
	/// <param name="path">The path to normalize.</param>
	/// <param name="normalizedPath">The normalized path when successful; otherwise, an empty string.</param>
	/// <returns>
	/// <see langword="true"/> when the path is fully qualified and could be normalized;
	/// <see langword="false"/> for a <see langword="null"/>, blank, relative, or unnormalizable path.
	/// </returns>
	public static bool TryNormalize(string? path, out string normalizedPath) => LocalPathRules.TryNormalize(path, out normalizedPath);

	/// <summary>Validates and normalizes a list of workspace root directories.</summary>
	/// <param name="rootPaths">The root directories to normalize.</param>
	/// <param name="comparison">The policy that decides which roots are duplicates.</param>
	/// <param name="rootPathsExpression">
	/// The argument expression of <paramref name="rootPaths"/>, which the compiler supplies and the exceptions report as the parameter name,
	/// so a constructor that forwards its own parameter reports that parameter. A caller omits it.
	/// </param>
	/// <returns>The normalized root paths in caller order, which preserve the caller's casing.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="rootPaths"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="rootPaths"/> contains a <see langword="null"/>, empty, whitespace-only, relative, or duplicate entry,
	/// where duplicates follow <paramref name="comparison"/>.
	/// </exception>
	/// <remarks>
	/// An empty list is valid and models a folderless session without a workspace root.
	/// </remarks>
	public static IReadOnlyList<string> NormalizeRoots(
		IReadOnlyList<string> rootPaths,
		LocalPathComparisonPolicy comparison,
		[CallerArgumentExpression(nameof(rootPaths))] string? rootPathsExpression = null
	) => LocalPathRules.NormalizeRoots(rootPaths, comparison.Comparer, rootPathsExpression ?? nameof(rootPaths));

	/// <summary>Determines whether a normalized path is a directory itself or lies beneath it.</summary>
	/// <param name="normalizedPath">The normalized path to test.</param>
	/// <param name="normalizedDirectory">The normalized directory, which ends in a separator only when it is a drive root.</param>
	/// <param name="comparison">The policy that decides whether two spellings name one location.</param>
	/// <returns>
	/// <see langword="true"/> when the path equals the directory or continues it past a directory separator; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="normalizedPath"/> or <paramref name="normalizedDirectory"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// A sibling that shares a name prefix with the directory is not beneath it. Both arguments must be normalized: a path that spells a separator
	/// with the alternate character, where the platform has one, does not continue the directory.
	/// </remarks>
	public static bool IsDirectoryOrDescendant(string normalizedPath, string normalizedDirectory, LocalPathComparisonPolicy comparison)
	{
		ArgumentNullException.ThrowIfNull(normalizedPath);
		ArgumentNullException.ThrowIfNull(normalizedDirectory);

		if (!normalizedPath.StartsWith(normalizedDirectory, comparison.Comparison))
			return false;

		// A drive root already ends with a separator, and any other directory, a share root included, needs a separator right after it
		// so a sibling that shares its name prefix cannot match.
		return normalizedPath.Length == normalizedDirectory.Length
			|| Path.EndsInDirectorySeparator(normalizedDirectory)
			|| normalizedPath[normalizedDirectory.Length] == Path.DirectorySeparatorChar;
	}

	/// <summary>Describes a folder by its path below the workspace root that holds it, for a list that shows where a file lives.</summary>
	/// <param name="normalizedRoots">The normalized workspace roots.</param>
	/// <param name="normalizedFolder">The normalized folder to describe.</param>
	/// <param name="comparison">The policy that decides whether two spellings name one location.</param>
	/// <returns>
	/// The path below the first root that holds the folder, written with slashes, with the name of the root in front when there are several roots.
	/// A root itself is <see langword="null"/> when there is one root and its name when there are several. A folder that no root holds
	/// is its normalized path.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="normalizedRoots"/>, an entry of it or <paramref name="normalizedFolder"/> is <see langword="null"/>.
	/// </exception>
	public static string? DescribeFolder(IReadOnlyList<string> normalizedRoots, string normalizedFolder, LocalPathComparisonPolicy comparison)
	{
		ArgumentNullException.ThrowIfNull(normalizedRoots);
		ArgumentNullException.ThrowIfNull(normalizedFolder);

		foreach (string root in normalizedRoots)
		{
			ArgumentNullException.ThrowIfNull(root, nameof(normalizedRoots));

			if (!IsDirectoryOrDescendant(normalizedFolder, root, comparison))
				continue;

			string below =
				normalizedFolder.Length == root.Length ? string.Empty : normalizedFolder[root.Length..].TrimStart(Path.DirectorySeparatorChar);

			if (Path.DirectorySeparatorChar != '/')
				below = below.Replace(Path.DirectorySeparatorChar, '/');

			if (normalizedRoots.Count > 1)
			{
				string rootName = Path.GetFileName(root);

				if (rootName.Length == 0)
					rootName = root;

				return below.Length == 0 ? rootName : $"{rootName}/{below}";
			}

			return below.Length == 0 ? null : below;
		}

		return normalizedFolder;
	}
}
