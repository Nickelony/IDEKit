using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the shared <see cref="SignatureHelpTriggerKind"/> vocabulary onto the protocol trigger kind.</summary>
internal static class SignatureHelpTriggerKindConversion
{
	/// <summary>Resolves a trigger kind to the corresponding protocol member.</summary>
	/// <param name="kind">The trigger kind of the request.</param>
	/// <returns>
	/// The matching protocol member, or <see cref="Lsp.SignatureHelpTriggerKind.Invoked"/> for a value the vocabulary does not define,
	/// because the protocol defines no other numbers. <c>SignatureHelpContext</c> rejects such a value at construction.
	/// </returns>
	internal static Lsp.SignatureHelpTriggerKind ToProtocol(SignatureHelpTriggerKind kind)
	{
		return kind switch
		{
			SignatureHelpTriggerKind.Invoked => Lsp.SignatureHelpTriggerKind.Invoked,
			SignatureHelpTriggerKind.TriggerCharacter => Lsp.SignatureHelpTriggerKind.TriggerCharacter,
			SignatureHelpTriggerKind.ContentChange => Lsp.SignatureHelpTriggerKind.ContentChange,
			_ => Lsp.SignatureHelpTriggerKind.Invoked,
		};
	}
}
