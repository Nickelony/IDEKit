using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the editor's completion context onto the protocol context that a <c>textDocument/completion</c> request carries.</summary>
internal static class CompletionContextConversion
{
	/// <summary>Maps a completion context to the protocol context.</summary>
	/// <param name="context">The context that started the completion request.</param>
	/// <returns>A trigger-character context for a typed character, and an invoked context otherwise.</returns>
	/// <remarks>
	/// The protocol context distinguishes an invoked request from one a typed character triggered; a host-defined trigger
	/// has no protocol counterpart, so it reads as invoked.
	/// </remarks>
	internal static CompletionContextPayload ToProtocol(CompletionContext context)
	{
		return context.TriggerCharacter is { } triggerCharacter
			? new CompletionContextPayload(TriggerKind: CompletionTriggerKind.TriggerCharacter, triggerCharacter.ToString())
			: new CompletionContextPayload(TriggerKind: CompletionTriggerKind.Invoked);
	}
}
