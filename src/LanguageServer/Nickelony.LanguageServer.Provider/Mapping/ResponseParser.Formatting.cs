namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	/// <summary>
	/// Parses document-formatting edits from an LSP formatting response.
	/// </summary>
	/// <param name="response">The text edit payloads from the formatting response, or <see langword="null"/> when unavailable.</param>
	/// <returns>The formatted text edits, or an empty list when no edits are present.</returns>
	internal static IReadOnlyList<TextEdit> ParseDocumentFormattingEdits(IReadOnlyList<TextEditPayload>? response)
	{
		if (response is null)
			return [];

		var textEdits = new List<TextEdit>();
		WorkspaceEditConversion.AppendTextEdits(response, textEdits);
		return textEdits;
	}
}
