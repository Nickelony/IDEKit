using System.Runtime.CompilerServices;

namespace Nickelony.Infrastructure;

/// <summary>
/// The local-path identity rules that Core and Client share: the platform's case default and the normalization of a path and of a root list.
/// </summary>
/// <remarks>
/// The rules compile into each package from this one source file, because Client references no other package and a rule kept in two copies drifts.
/// A path is normalized for identity, so it must be fully qualified: resolving a relative path against the process's current directory would bind
/// a document's identity to ambient process state.
/// </remarks>
internal static class LocalPathRules
{
	/// <summary>Indicates whether local-path identity ignores character casing on this operating system.</summary>
	/// <remarks>
	/// <see langword="true"/> on Windows and macOS. The value reads the operating system and never probes the volume.
	/// </remarks>
	internal static bool IgnoresCaseByDefault { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

	/// <summary>The comparer that applies the operating system's case default to normalized local paths.</summary>
	internal static StringComparer DefaultComparer => IgnoresCaseByDefault ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	/// <summary>Normalizes a fully qualified local path into the one spelling used as document identity.</summary>
	/// <param name="path">The fully qualified path to normalize.</param>
	/// <param name="paramName">
	/// The name the exceptions report for <paramref name="path"/>; the compiler fills it with the argument expression, so a caller that forwards
	/// its own parameter reports that parameter's name.
	/// </param>
	/// <returns>The full path with unified separators and no trailing directory separator.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="path"/> is empty, whitespace-only, not fully qualified, or invalid on the current platform.
	/// </exception>
	/// <exception cref="PathTooLongException">The resolved path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">The path contains a colon that is not part of a volume identifier.</exception>
	/// <remarks>
	/// The result preserves the input casing, so it is a display and protocol value, not a case-insensitive identity key: compare normalized paths
	/// with a comparer that applies the host's case policy. The path is not Unicode-normalized, so the NFC and NFD spellings of one macOS file
	/// are two paths.
	/// </remarks>
	internal static string Normalize(string path, [CallerArgumentExpression(nameof(path))] string? paramName = null)
	{
		ArgumentNullException.ThrowIfNull(path, paramName);

		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("The path must not be empty.", paramName);

		if (!Path.IsPathFullyQualified(path))
			throw new ArgumentException("The path must be fully qualified.", paramName);

		string unifiedPath =
			Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
				? path
				: path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(unifiedPath));
	}

	/// <summary>Tries to normalize a local path without throwing for invalid input.</summary>
	/// <param name="path">The path to normalize.</param>
	/// <param name="normalizedPath">The normalized path when successful; otherwise, an empty string.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="path"/> is fully qualified and could be normalized;
	/// <see langword="false"/> for a <see langword="null"/>, blank, relative, or unnormalizable path.
	/// </returns>
	internal static bool TryNormalize(string? path, out string normalizedPath)
	{
		normalizedPath = string.Empty;

		if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
			return false;

		try
		{
			normalizedPath = Normalize(path);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
		{
			return false;
		}
	}

	/// <summary>Validates and normalizes a list of workspace root directories.</summary>
	/// <param name="rootPaths">The root directories to normalize.</param>
	/// <param name="comparer">The comparer that expresses the host's case policy, which decides what counts as a duplicate.</param>
	/// <param name="paramName">The name the exceptions report for <paramref name="rootPaths"/>, which is the caller's own parameter.</param>
	/// <returns>The normalized root paths in caller order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="rootPaths"/> or <paramref name="comparer"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="rootPaths"/> contains a <see langword="null"/>, empty, whitespace-only, relative, or duplicate entry.
	/// </exception>
	/// <remarks>
	/// An empty list is valid and models a folderless session. The returned paths preserve the caller's casing.
	/// </remarks>
	internal static string[] NormalizeRoots(IReadOnlyList<string> rootPaths, StringComparer comparer, string paramName)
	{
		ArgumentNullException.ThrowIfNull(rootPaths, paramName);
		ArgumentNullException.ThrowIfNull(comparer);

		string[] normalizedRootPaths = new string[rootPaths.Count];
		var seenRootPaths = new HashSet<string>(comparer);

		for (int i = 0; i < rootPaths.Count; i++)
		{
			string rootPath = rootPaths[i];

			if (string.IsNullOrWhiteSpace(rootPath))
				throw new ArgumentException("Workspace root directory paths must not be null, empty, or whitespace-only.", paramName);

			if (!Path.IsPathFullyQualified(rootPath))
				throw new ArgumentException($"The workspace root directory path '{rootPath}' must be fully qualified.", paramName);

			string normalizedRootPath = Normalize(rootPath, paramName);

			if (!seenRootPaths.Add(normalizedRootPath))
				throw new ArgumentException($"The workspace root directory list contains the duplicate path '{normalizedRootPath}'.", paramName);

			normalizedRootPaths[i] = normalizedRootPath;
		}

		return normalizedRootPaths;
	}
}
