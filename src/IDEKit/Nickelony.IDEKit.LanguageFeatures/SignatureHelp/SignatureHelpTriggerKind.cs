namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>Specifies how signature help was triggered.</summary>
/// <remarks>
/// The values mirror the LSP <c>SignatureHelpTriggerKind</c> enumeration and start at 1, so zero is not defined; a host must supply a defined
/// member rather than the default-initialized value. The vocabulary is closed, unlike the open <see cref="Completion.CompletionContext"/> vocabulary,
/// because signature triggering is a protocol context.
/// </remarks>
public enum SignatureHelpTriggerKind
{
	/// <summary>Signature help was invoked manually by the user or by a command.</summary>
	Invoked = 1,

	TriggerCharacter = 2,

	/// <summary>Signature help was triggered by the caret moving or by the document content changing.</summary>
	ContentChange = 3,
}
