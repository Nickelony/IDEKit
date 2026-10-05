using System.Collections.ObjectModel;
using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Associates one parsed shared diagnostic with the raw protocol payload the server sent.</summary>
/// <param name="Diagnostic">The parsed diagnostic used for display and offset-based queries.</param>
/// <param name="Payload">
/// The raw protocol diagnostic exactly as received. A consumer that echoes it back remaps only the range; every other field is preserved verbatim.
/// </param>
/// <remarks>
/// A payload reconstructed from the cache (for example a code-action request context) can echo the protocol fields the shared diagnostic
/// does not model - <c>data</c>, <c>tags</c>, <c>codeDescription</c> and <c>relatedInformation</c>.
/// </remarks>
internal readonly record struct DiagnosticEntry(Diagnostic Diagnostic, DiagnosticPayload Payload)
{
	private static readonly ReadOnlyCollection<Diagnostic> s_emptyProjection = Array.AsReadOnly<Diagnostic>([]);

	/// <summary>
	/// The protocol code element exactly as received, or a default (undefined) element when the server sent none or sent a JSON kind
	/// other than a string or number.
	/// </summary>
	/// <remarks>
	/// The display attribution is <see cref="Diagnostic.Code"/>.
	/// </remarks>
	internal JsonElement Code => Payload.Code is { } code && code.ValueKind is JsonValueKind.String or JsonValueKind.Number ? code : default;

	internal bool HasCode => Code.ValueKind is JsonValueKind.String or JsonValueKind.Number;

	/// <summary>
	/// Projects diagnostic entries to a read-only snapshot of the shared diagnostics they wrap, preserving entry order and the shared
	/// diagnostic instances.
	/// </summary>
	/// <param name="entries">The entries to project.</param>
	/// <returns>The shared diagnostics in entry order, backed by a read-only collection.</returns>
	internal static ReadOnlyCollection<Diagnostic> ProjectDiagnostics(IReadOnlyList<DiagnosticEntry> entries)
	{
		if (entries.Count == 0)
			return s_emptyProjection;

		var diagnostics = new Diagnostic[entries.Count];

		for (int i = 0; i < entries.Count; i++)
			diagnostics[i] = entries[i].Diagnostic;

		return Array.AsReadOnly(diagnostics);
	}
}
