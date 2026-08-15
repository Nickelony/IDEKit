namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Describes the context in which a signature help request was triggered.
/// </summary>
/// <remarks>
/// Providers keep the selected overload stable across content changes and caret moves: a retriggered
/// request carries the previously shown payload, whose active signature index reflects the user's
/// overload navigation.
/// </remarks>
public sealed class TextSignatureHelpContext
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelpContext"/> class.
	/// </summary>
	/// <param name="triggerKind">The action that caused signature help to be triggered.</param>
	/// <param name="triggerCharacter">
	/// The character that triggered signature help. The value is retained only for
	/// <see cref="TextSignatureHelpTriggerKind.TriggerCharacter"/>, because it is undefined for the
	/// other trigger kinds; a blank value is treated as absent and surrounding whitespace is trimmed.
	/// </param>
	/// <param name="isRetrigger">
	/// <see langword="true"/> when signature help was already showing when it was triggered;
	/// retriggers occur when signature help is active and the user types a trigger character, moves
	/// the caret, or changes the document content.
	/// </param>
	/// <param name="activeSignatureHelp">
	/// The currently active signature help payload, when available. The value is carried independently
	/// of <paramref name="isRetrigger"/>: LSP defines it for retriggers, but a host may also supply it
	/// when it restores or re-requests signature help without a retrigger.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="triggerKind"/> is not a defined <see cref="TextSignatureHelpTriggerKind"/> value.
	/// </exception>
	public TextSignatureHelpContext(
		TextSignatureHelpTriggerKind triggerKind = TextSignatureHelpTriggerKind.Invoked,
		string? triggerCharacter = null,
		bool isRetrigger = false,
		TextSignatureHelp? activeSignatureHelp = null)
	{
		if (!Enum.IsDefined(triggerKind))
			throw new ArgumentOutOfRangeException(nameof(triggerKind), triggerKind, "The trigger kind is not defined.");

		TriggerKind = triggerKind;
		TriggerCharacter = triggerKind == TextSignatureHelpTriggerKind.TriggerCharacter && !string.IsNullOrWhiteSpace(triggerCharacter)
			? triggerCharacter.Trim()
			: null;
		IsRetrigger = isRetrigger;
		ActiveSignatureHelp = activeSignatureHelp;
	}

	/// <summary>
	/// Gets the action that caused signature help to be triggered.
	/// </summary>
	public TextSignatureHelpTriggerKind TriggerKind { get; }

	/// <summary>
	/// Gets the trigger character, or <see langword="null"/> when the trigger kind is not
	/// <see cref="TextSignatureHelpTriggerKind.TriggerCharacter"/> or no character was supplied.
	/// </summary>
	public string? TriggerCharacter { get; }

	/// <summary>
	/// Gets a value indicating whether signature help was already showing when it was triggered.
	/// </summary>
	public bool IsRetrigger { get; }

	/// <summary>
	/// Gets the currently active signature help payload, when available.
	/// </summary>
	/// <remarks>
	/// The value is independent of <see cref="IsRetrigger"/>; see the constructor for details.
	/// </remarks>
	public TextSignatureHelp? ActiveSignatureHelp { get; }
}
