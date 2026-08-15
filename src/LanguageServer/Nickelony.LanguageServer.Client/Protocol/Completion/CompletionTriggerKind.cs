namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes how a completion request was triggered, using the LSP <c>CompletionTriggerKind</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value; callers should treat unknown values
/// like <see cref="Invoked"/> when they cannot act on them.
/// </remarks>
public enum CompletionTriggerKind
{
	/// <summary>
	/// Completion was invoked explicitly (for example by a manual trigger).
	/// </summary>
	Invoked = 1,

	/// <summary>
	/// Completion was triggered by a trigger character.
	/// </summary>
	TriggerCharacter = 2,

	/// <summary>
	/// Completion was re-triggered for a previously incomplete completion list.
	/// </summary>
	TriggerForIncompleteCompletions = 3
}
