namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Checks the document URI that a payload record requires.</summary>
internal static class DocumentUriValidation
{
	/// <summary>Returns the URI after checking that it is present.</summary>
	/// <param name="uri">The URI to check.</param>
	/// <returns><paramref name="uri"/>, unchanged.</returns>
	/// <exception cref="ArgumentException"><paramref name="uri"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
	internal static string RequireText(string? uri)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(uri, nameof(uri));
		return uri;
	}
}
