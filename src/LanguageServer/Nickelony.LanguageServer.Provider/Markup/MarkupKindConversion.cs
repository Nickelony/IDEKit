using Nickelony.IDEKit.LanguageFeatures.Markup;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the protocol markup kind onto the shared <see cref="MarkupKind"/> vocabulary and back.</summary>
internal static class MarkupKindConversion
{
	/// <summary>Resolves a protocol markup kind to the corresponding member.</summary>
	/// <param name="kind">The protocol markup kind.</param>
	/// <returns>
	/// The matching member, or <see cref="MarkupKind.PlainText"/> for a value the protocol does not define, because the protocol reads
	/// an unrecognized kind as plain text.
	/// </returns>
	internal static MarkupKind FromProtocol(Lsp.MarkupKind kind)
	{
		return kind switch
		{
			Lsp.MarkupKind.PlainText => MarkupKind.PlainText,
			Lsp.MarkupKind.Markdown => MarkupKind.Markdown,
			_ => MarkupKind.PlainText,
		};
	}

	/// <summary>Resolves a shared markup kind to the corresponding protocol member.</summary>
	/// <param name="kind">The shared markup kind.</param>
	/// <returns>The matching protocol member, or <see cref="Lsp.MarkupKind.PlainText"/> for a value the vocabulary does not define.</returns>
	internal static Lsp.MarkupKind ToProtocol(MarkupKind kind)
	{
		return kind switch
		{
			MarkupKind.PlainText => Lsp.MarkupKind.PlainText,
			MarkupKind.Markdown => Lsp.MarkupKind.Markdown,
			_ => Lsp.MarkupKind.PlainText,
		};
	}
}
