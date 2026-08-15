namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes how a signature-help request was triggered, using the LSP <c>SignatureHelpTriggerKind</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value; callers should treat unknown values
/// like <see cref="Invoked"/> when they cannot act on them.
/// </remarks>
public enum SignatureHelpTriggerKind
{
	/// <summary>
	/// Signature help was invoked manually by the user or by a command.
	/// </summary>
	Invoked = 1,

	/// <summary>
	/// Signature help was triggered by a trigger character.
	/// </summary>
	TriggerCharacter = 2,

	/// <summary>
	/// Signature help was triggered by the caret moving or by the document content changing.
	/// </summary>
	ContentChange = 3
}
