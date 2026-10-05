using Nickelony.Infrastructure;

namespace Nickelony.LanguageServer.Client;

/// <summary>Converts between local file paths and the file URIs of the LSP's <c>DocumentUri</c>.</summary>
/// <remarks>
/// The path side follows the shared local-path rules, which are fully qualified paths only: a relative path is rejected instead of being resolved
/// against the process's current directory. A host that compares the resulting paths applies its own case policy.
/// </remarks>
public static class DocumentUriConversion
{
	/// <summary>Converts a fully qualified local file path into a normalized file URI for language-server requests.</summary>
	/// <param name="filePath">The fully qualified local file path to convert.</param>
	/// <returns>The absolute file URI.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty, whitespace-only, relative, or invalid on the current platform.
	/// </exception>
	/// <exception cref="PathTooLongException">The resolved absolute path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">The path contains a colon that is not part of a volume identifier.</exception>
	/// <exception cref="UriFormatException">The resolved path cannot be represented as a file URI.</exception>
	/// <remarks>
	/// <para>
	/// The file URI is built textually from an explicitly escaped path: the <see cref="Uri"/> DOS-path parser decodes a literal percent-hex
	/// sequence in a file name (<c>a%41b.txt</c> would become <c>aAb.txt</c>). The percent, hash, and question-mark characters are therefore escaped
	/// before the URI is parsed, which keeps path-to-URI-to-path round trips lossless.
	/// </para>
	/// <para>
	/// A UNC path keeps its two leading separators as the URI authority, while drive-letter and rooted POSIX paths are authority-less and carry
	/// exactly three slashes.
	/// </para>
	/// </remarks>
	public static string CreateFileUri(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		return BuildFileUri(LocalPathRules.Normalize(filePath));
	}

	/// <summary>Tries to extract and normalize a local file path from a file URI.</summary>
	/// <param name="uriText">The file URI text.</param>
	/// <param name="normalizedFilePath">The normalized local file path when successful.</param>
	/// <returns><see langword="true"/> when a local file path was resolved; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A file URI with a remote authority (<c>file://server/share/...</c>) becomes a UNC path on Windows. On other hosts the authority
	/// cannot be represented as a local path, so the URI is rejected instead of silently producing a colliding local path. An empty authority
	/// and the <c>localhost</c> authority are local on every platform.
	/// </remarks>
	public static bool TryGetLocalPath(string? uriText, out string normalizedFilePath)
	{
		normalizedFilePath = string.Empty;

		if (string.IsNullOrWhiteSpace(uriText) || !Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri) || uri?.IsFile != true)
			return false;

		try
		{
			normalizedFilePath = NormalizeLocalPath(uri, supportsUncAuthority: OperatingSystem.IsWindows());
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UriFormatException)
		{
			return false;
		}
	}

	/// <summary>Builds the absolute file URI for one normalized local path.</summary>
	/// <param name="normalizedPath">The normalized absolute local path.</param>
	/// <returns>The absolute file URI.</returns>
	/// <remarks>
	/// Internal so the platform-shaped URI construction can be tested with POSIX and UNC inputs on every platform; the production entry point
	/// is <see cref="CreateFileUri"/>.
	/// </remarks>
	internal static string BuildFileUri(string normalizedPath)
	{
		string pathText = normalizedPath;

		// Extended-length prefixes do not form a usable URI authority; strip them so the remaining path
		// selects the drive-letter or UNC shape.
		if (pathText.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
			pathText = @"\\" + pathText[8..];
		else if (pathText.StartsWith(@"\\?\", StringComparison.Ordinal))
			pathText = pathText[4..];

		string escapedPath = pathText
			.Replace('\\', '/')
			.Replace("%", "%25", StringComparison.Ordinal)
			.Replace("#", "%23", StringComparison.Ordinal)
			.Replace("?", "%3F", StringComparison.Ordinal);

		string uriText = escapedPath.StartsWith("//", StringComparison.Ordinal) ? "file:" + escapedPath : "file:///" + escapedPath.TrimStart('/');

		return new Uri(uriText).AbsoluteUri;
	}

	/// <summary>Normalizes a file URI into the absolute local-path form used by the language-server client.</summary>
	/// <param name="uri">The file URI to normalize.</param>
	/// <param name="supportsUncAuthority">Whether a remote file-URI authority can be represented as a local path.</param>
	/// <returns>The normalized absolute local path.</returns>
	/// <exception cref="ArgumentException">
	/// The URI is not an absolute file URI, or it carries a remote authority that cannot be represented as a local path on the current platform.
	/// </exception>
	/// <remarks>
	/// Internal so the authority policy can be tested with POSIX and UNC inputs on every platform; the production entry point
	/// is <see cref="TryGetLocalPath"/>.
	/// </remarks>
	internal static string NormalizeLocalPath(Uri uri, bool supportsUncAuthority)
	{
		if (!uri.IsAbsoluteUri || !uri.IsFile)
			throw new ArgumentException("The URI must be an absolute file URI.", nameof(uri));

		if (!supportsUncAuthority && !IsLocalFileUriHost(uri.Host))
		{
			throw new ArgumentException(
				"The file URI names a remote authority that cannot be represented as a local path on this platform.",
				nameof(uri)
			);
		}

		string localPath = uri.LocalPath;

		// An escaped drive colon (file:///c%3A/...), which VS Code and the Lua language server send, hides the drive from the URI parser, so the
		// path arrives as /c:/... behind a separator that a drive path does not have.
		if (localPath.Length >= 3 && localPath[0] is '/' or '\\' && char.IsAsciiLetter(localPath[1]) && localPath[2] == ':')
			localPath = localPath[1..];

		return LocalPathRules.Normalize(localPath);
	}

	/// <summary>Reports whether a file-URI host names the local machine rather than a remote authority.</summary>
	/// <param name="host">The <see cref="Uri.Host"/> of the file URI.</param>
	/// <returns><see langword="true"/> when the host is empty or the local host name.</returns>
	private static bool IsLocalFileUriHost(string host) => host.Length == 0 || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
}
