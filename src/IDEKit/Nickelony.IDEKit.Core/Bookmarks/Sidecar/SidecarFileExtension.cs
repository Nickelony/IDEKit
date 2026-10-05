using System.Buffers;

namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>Validates and normalizes the sidecar file extension a host supplies, independent of the document path it is appended to.</summary>
/// <remarks>
/// Validation is platform-independent: the rejected characters are the Windows-reserved set plus the path separators, so a name
/// is treated the same everywhere. A usable extension is non-blank, does not end with a dot, and contains no path separators, control characters,
/// or the characters ':', '*', '?', '"', '&lt;', '&gt;', or '|'. An all-dots extension resolves to the document itself and is rejected,
/// as is a trailing dot.
/// </remarks>
internal static class SidecarFileExtension
{
	/// <summary>The characters rejected in a sidecar extension on every platform: the Windows-reserved set plus the path separators.</summary>
	private static readonly SearchValues<char> s_reservedCharacters = SearchValues.Create(":*?\"<>|\\/");

	/// <summary>Validates a sidecar extension and returns its trimmed form.</summary>
	/// <param name="sidecarExtension">The extension to validate, with or without a leading dot.</param>
	/// <returns>The trimmed extension, keeping a leading dot only when the input had one.</returns>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> is blank or does not form a usable file extension.</exception>
	internal static string Normalize(string sidecarExtension)
	{
		string extension = sidecarExtension.Trim();

		if (!IsUsable(extension))
		{
			throw new ArgumentException(
				"The sidecar extension must be a file-name extension such as \".bkmrk\": it must not be blank, must not end with a dot, and must not contain path separators, control characters, or the characters ':', '*', '?', '\"', '<', '>', or '|'.",
				nameof(sidecarExtension)
			);
		}

		return extension;
	}

	/// <summary>Determines whether a trimmed extension is usable: it must not resolve to the document itself or to a different directory.</summary>
	private static bool IsUsable(string extension)
	{
		if (extension.TrimStart('.').Length == 0 || extension.EndsWith('.'))
			return false;

		if (extension.AsSpan().IndexOfAny(s_reservedCharacters) >= 0)
			return false;

		foreach (char character in extension)
		{
			if (char.IsControl(character))
				return false;
		}

		return true;
	}
}
