using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Maps protocol symbol kinds onto the shared <see cref="TextDocumentSymbolKind"/> vocabulary.
/// </summary>
/// <remarks>
/// The shared enum's member values mirror the protocol numerics one-to-one, so the bridge validates
/// the protocol range and casts. The protocol defines no default symbol kind, so a caller chooses its
/// own fallback through <see cref="TryFromLspKind"/> instead of receiving one silently.
/// </remarks>
public static class TextDocumentSymbolKindConversion
{
	/// <summary>
	/// Resolves a protocol <c>SymbolKind</c> numeric value to the corresponding member.
	/// </summary>
	/// <param name="lspKind">The numeric protocol symbol kind.</param>
	/// <returns>The matching member.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="lspKind"/> is not a defined protocol symbol kind.
	/// </exception>
	public static TextDocumentSymbolKind FromLspKind(int lspKind)
		=> TryFromLspKind(lspKind, out TextDocumentSymbolKind kind)
			? kind
			: throw new ArgumentOutOfRangeException(nameof(lspKind), lspKind, "The value is not a defined protocol symbol kind.");

	/// <summary>
	/// Attempts to resolve a protocol <c>SymbolKind</c> numeric value to the corresponding member.
	/// </summary>
	/// <param name="lspKind">The numeric protocol symbol kind.</param>
	/// <param name="kind">
	/// Receives the matching member when the method returns <see langword="true"/>; otherwise, the
	/// default value.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="lspKind"/> is a defined protocol symbol kind
	/// (1-26); otherwise, <see langword="false"/>.
	/// </returns>
	public static bool TryFromLspKind(int lspKind, out TextDocumentSymbolKind kind)
	{
		if (lspKind is >= 1 and <= 26)
		{
			kind = (TextDocumentSymbolKind)lspKind;
			return true;
		}

		kind = default;
		return false;
	}
}
