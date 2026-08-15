using System.Buffers;

namespace Nickelony.IDEKit.Core.Persistence;

/// <summary>
/// Validates and normalizes the sidecar file extension a host supplies, independent of the document
/// path it is appended to.
/// </summary>
/// <remarks>
/// <para>
/// This is the extension policy of <see cref="SidecarLineFile"/>, split out so the persistence kernel
/// carries no naming rule. Validation is platform-independent: the same extensions are accepted on
/// every operating system, so a configuration accepted on one platform is not rejected on another.
/// The rejected characters are the Windows-reserved set plus the path separators - the strictest of
/// the supported platforms - kept as one superset so a name is treated the same everywhere.
/// </para>
/// <para>
/// A usable extension is non-blank, does not end with a dot, and contains no path separators, control
/// characters, or the characters ':', '*', '?', '"', '&lt;', '&gt;', or '|'. An extension whose only
/// characters are dots resolves to the document itself and is rejected; a trailing dot is rejected
/// because some file systems strip it, so it would resolve to the same file as the untrailed form.
/// The extension is accepted with or without a leading dot and is trimmed before use.
/// </para>
/// </remarks>
internal static class SidecarFileExtension
{
	/// <summary>
	/// The characters rejected in a sidecar extension on every platform - the Windows-reserved set plus
	/// the path separators - so validation is the same superset everywhere and is not platform-dependent.
	/// Control characters are rejected in addition to this set.
	/// </summary>
	private static readonly SearchValues<char> s_reservedCharacters = SearchValues.Create(":*?\"<>|\\/");

	/// <summary>
	/// Validates a sidecar extension and returns its trimmed form.
	/// </summary>
	/// <param name="sidecarExtension">The extension to validate, with or without a leading dot.</param>
	/// <returns>The trimmed extension, ready to be appended to a document path.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="sidecarExtension"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="sidecarExtension"/> is blank or does not form a usable file extension.
	/// </exception>
	internal static string Normalize(string sidecarExtension)
	{
		ArgumentNullException.ThrowIfNull(sidecarExtension);

		string extension = sidecarExtension.Trim();

		if (!IsUsable(extension))
		{
			throw new ArgumentException(
				"The sidecar extension must be a file-name extension such as \".bkmrk\": it must not be blank, must not end with a dot, and must not contain path separators, control characters, or the characters ':', '*', '?', '\"', '<', '>', or '|'.",
				nameof(sidecarExtension));
		}

		return extension;
	}

	/// <summary>
	/// Determines whether a trimmed extension is usable: it must not resolve to the document itself or
	/// to a different directory.
	/// </summary>
	/// <param name="extension">The trimmed extension to validate.</param>
	/// <returns><see langword="true"/> when the extension can be appended to a document path.</returns>
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
