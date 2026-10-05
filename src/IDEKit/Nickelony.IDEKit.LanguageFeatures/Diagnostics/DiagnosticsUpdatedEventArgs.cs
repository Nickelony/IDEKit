namespace Nickelony.IDEKit.LanguageFeatures.Diagnostics;

/// <summary>Provides the data for the <see cref="IDiagnosticsSource.DiagnosticsUpdated"/> event.</summary>
/// <remarks>
/// The constructor copies the supplied sequence, so the event payload is owned by this instance: the list remains valid after the callback returns
/// and cannot be mutated through the source collection. The list may be empty.
/// </remarks>
public sealed class DiagnosticsUpdatedEventArgs : EventArgs
{
	/// <param name="documentId">The identifier of the document the diagnostics belong to.</param>
	/// <param name="diagnostics">The document's current diagnostics.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> or <paramref name="diagnostics"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="diagnostics"/> contains a <see langword="null"/> element.</exception>
	public DiagnosticsUpdatedEventArgs(string documentId, IReadOnlyList<Diagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		ArgumentNullException.ThrowIfNull(diagnostics);

		DocumentId = documentId;
		Diagnostics = CollectionSnapshots.Capture(diagnostics, "diagnostics", nameof(diagnostics));
	}

	/// <summary>The identifier of the document the diagnostics belong to.</summary>
	public string DocumentId { get; }

	/// <summary>The document's current diagnostics, an owned immutable snapshot.</summary>
	public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
