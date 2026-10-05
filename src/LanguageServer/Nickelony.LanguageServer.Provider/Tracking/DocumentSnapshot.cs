namespace Nickelony.LanguageServer.Provider;

/// <summary>Captures the current synchronized state of a tracked document.</summary>
internal sealed class DocumentSnapshot
{
	/// <param name="filePath">The normalized local file path.</param>
	/// <param name="uri">The corresponding file URI sent to the language server.</param>
	/// <param name="content">The current document text.</param>
	/// <param name="version">The local synchronization version.</param>
	/// <param name="documentId">
	/// The id the host supplied for the document, or <see langword="null"/> when it equals <paramref name="filePath"/>.
	/// </param>
	internal DocumentSnapshot(string filePath, string uri, string content, int version, string? documentId = null)
	{
		FilePath = filePath;
		Uri = uri;
		Content = content;
		Version = version;
		DocumentId = documentId ?? filePath;
	}

	/// <summary>The normalized local file path.</summary>
	internal string FilePath { get; }

	/// <summary>The id the host supplied for the document, which the provider's document-scoped events carry.</summary>
	internal string DocumentId { get; }

	/// <summary>The file URI sent to the language server for this document.</summary>
	internal string Uri { get; }

	internal string Content { get; }

	/// <summary>The local synchronization version.</summary>
	internal int Version { get; }
}
