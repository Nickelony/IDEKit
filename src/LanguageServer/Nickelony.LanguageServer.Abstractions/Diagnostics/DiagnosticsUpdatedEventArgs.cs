using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Provides the data for the <see cref="ILanguageServerIntelliSenseProvider.DiagnosticsUpdated"/> event.
/// </summary>
/// <remarks>
/// The constructor copies the supplied sequence, so the event payload is owned by this instance: the list
/// remains valid after the callback returns and cannot be mutated through the source collection. The list may
/// be empty, meaning the document currently has no diagnostics.
/// </remarks>
public sealed class DiagnosticsUpdatedEventArgs : EventArgs
{
	private static readonly IReadOnlyList<TextDiagnostic> s_emptyDiagnostics = Array.AsReadOnly<TextDiagnostic>([]);

	/// <summary>
	/// Initializes a new instance of the <see cref="DiagnosticsUpdatedEventArgs"/> class.
	/// </summary>
	/// <param name="filePath">The local file path of the document whose diagnostics are updated.</param>
	/// <param name="diagnostics">The updated diagnostics snapshot for the document.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="diagnostics"/> is <see langword="null"/>.
	/// </exception>
	public DiagnosticsUpdatedEventArgs(string filePath, IReadOnlyList<TextDiagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(diagnostics);

		FilePath = filePath;
		Diagnostics = diagnostics.Count == 0 ? s_emptyDiagnostics : Array.AsReadOnly([.. diagnostics]);
	}

	/// <summary>
	/// Gets the local file path of the document whose diagnostics are updated.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the updated diagnostics snapshot for the document.
	/// </summary>
	public IReadOnlyList<TextDiagnostic> Diagnostics { get; }
}
