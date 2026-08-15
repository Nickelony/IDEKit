namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Specifies how signature help was triggered.
/// </summary>
/// <remarks>
/// The values mirror the LSP <c>SignatureHelpTriggerKind</c> enumeration, whose members start at 1,
/// so zero is not a defined trigger kind; hosts must supply a defined member instead of a
/// default-initialized value. The vocabulary is deliberately closed, unlike the open
/// <see cref="Completion.TextCompletionTrigger"/> vocabulary: signature triggering is a protocol
/// context (invoked, trigger character, or content change), while completion triggering is a host
/// interaction the shared vocabulary cannot enumerate.
/// </remarks>
public enum TextSignatureHelpTriggerKind
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
