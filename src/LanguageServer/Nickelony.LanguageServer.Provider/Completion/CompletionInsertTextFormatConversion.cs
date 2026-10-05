using Nickelony.IDEKit.LanguageFeatures.Completion;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the protocol insert-text format onto the shared <see cref="CompletionInsertTextFormat"/> vocabulary.</summary>
internal static class CompletionInsertTextFormatConversion
{
	/// <summary>Resolves a protocol insert-text format to the corresponding member.</summary>
	/// <param name="format">The protocol format, or <see langword="null"/> when the item omitted it.</param>
	/// <returns>
	/// The matching member, or <see cref="CompletionInsertTextFormat.PlainText"/> for an omitted or unrecognized format, because the protocol reads
	/// such a format as plain text.
	/// </returns>
	internal static CompletionInsertTextFormat FromProtocol(Lsp.InsertTextFormat? format)
	{
		return format switch
		{
			Lsp.InsertTextFormat.PlainText => CompletionInsertTextFormat.PlainText,
			Lsp.InsertTextFormat.Snippet => CompletionInsertTextFormat.Snippet,
			_ => CompletionInsertTextFormat.PlainText,
		};
	}
}
