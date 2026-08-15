using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Collections.ObjectModel;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Represents a diagnostics payload published by a language server for a document, together with the document
/// version it was produced for (<c>0</c> when the server reported none).
/// </summary>
internal sealed class PublishedDiagnostics
{
	/// <summary>
	/// Initializes a new instance of the <see cref="PublishedDiagnostics"/> class.
	/// </summary>
	/// <param name="filePath">The normalized file path associated with the diagnostics.</param>
	/// <param name="entries">The parsed diagnostic entries for that file, each paired with its raw protocol payload.</param>
	/// <param name="version">The synchronized document version that produced the diagnostics.</param>
	internal PublishedDiagnostics(string filePath, IReadOnlyList<DiagnosticEntry> entries, int version)
	{
		FilePath = filePath;
		Entries = entries;
		Diagnostics = DiagnosticEntry.ProjectDiagnostics(entries);
		Version = version;
	}

	/// <summary>
	/// Gets the normalized file path associated with the diagnostics.
	/// </summary>
	internal string FilePath { get; }

	/// <summary>
	/// Gets the parsed diagnostic entries, each pairing the shared diagnostic with its raw protocol payload
	/// (see <see cref="DiagnosticEntry"/>).
	/// </summary>
	internal IReadOnlyList<DiagnosticEntry> Entries { get; }

	/// <summary>
	/// Gets the shared diagnostics projection of <see cref="Entries"/>, in entry order.
	/// </summary>
	/// <remarks>
	/// The document store adopts this instance as its cached projection when it stores <see cref="Entries"/>, so the
	/// published payload and the cached read share one owned snapshot instead of projecting the same entries twice.
	/// </remarks>
	internal ReadOnlyCollection<TextDiagnostic> Diagnostics { get; }

	/// <summary>
	/// Gets the synchronized document version that produced the diagnostics, or <c>0</c> when the server did not report a version.
	/// </summary>
	internal int Version { get; }
}
