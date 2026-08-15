using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Associates one parsed shared diagnostic with the raw protocol payload the server sent.
/// </summary>
/// <remarks>
/// A payload reconstructed from the cache (for example a code-action request context) can echo the protocol fields
/// the shared diagnostic does not model - <c>data</c>, <c>tags</c>, <c>codeDescription</c> and
/// <c>relatedInformation</c>.
/// </remarks>
/// <param name="Diagnostic">The parsed diagnostic used for display and offset-based queries.</param>
/// <param name="Payload">
/// The raw protocol diagnostic exactly as received. A consumer that echoes it back remaps only the range; every
/// other field is preserved verbatim.
/// </param>
internal readonly record struct DiagnosticEntry(TextDiagnostic Diagnostic, DiagnosticPayload Payload)
{
	private static readonly ReadOnlyCollection<TextDiagnostic> s_emptyProjection = Array.AsReadOnly<TextDiagnostic>([]);

	/// <summary>
	/// Gets the protocol code element exactly as received, or a default (undefined) element when the server sent
	/// none or sent a JSON kind other than a string or number. The display attribution is
	/// <see cref="TextDiagnostic.Code"/>.
	/// </summary>
	internal JsonElement Code => Payload.Code is { } code && code.ValueKind is JsonValueKind.String or JsonValueKind.Number
		? code
		: default;

	/// <summary>
	/// Gets a value indicating whether the entry carries a raw protocol code element.
	/// </summary>
	internal bool HasCode => Code.ValueKind is JsonValueKind.String or JsonValueKind.Number;

	/// <summary>
	/// Projects diagnostic entries to a read-only snapshot of the shared diagnostics they wrap, preserving
	/// entry order and the shared diagnostic instances.
	/// </summary>
	/// <param name="entries">The entries to project.</param>
	/// <returns>The shared diagnostics in entry order, backed by a read-only collection.</returns>
	internal static ReadOnlyCollection<TextDiagnostic> ProjectDiagnostics(IReadOnlyList<DiagnosticEntry> entries)
	{
		if (entries.Count == 0)
			return s_emptyProjection;

		var diagnostics = new TextDiagnostic[entries.Count];

		for (int i = 0; i < entries.Count; i++)
			diagnostics[i] = entries[i].Diagnostic;

		return Array.AsReadOnly(diagnostics);
	}
}
