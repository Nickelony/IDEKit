namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes how a completion request was triggered, using the LSP <c>CompletionTriggerKind</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the defined range stays representable
/// as an unnamed enum value; callers should treat unknown values like <see cref="Invoked"/> when they cannot act on them.
/// </remarks>
public enum CompletionTriggerKind
{
	Invoked = 1,

	TriggerCharacter = 2,

	TriggerForIncompleteCompletions = 3,
}
