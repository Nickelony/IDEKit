namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>Describes the context in which a signature help request was triggered.</summary>
/// <remarks>
/// Providers keep the selected overload stable across content changes and caret moves: a retriggered request carries the payload shown
/// by the previous request, whose active signature index reflects the user's overload navigation.
/// </remarks>
public sealed class SignatureHelpContext
{
	/// <param name="triggerKind">The action that caused signature help to be triggered.</param>
	/// <param name="triggerCharacter">
	/// The character that triggered signature help. The value is retained only for <see cref="SignatureHelpTriggerKind.TriggerCharacter"/>,
	/// because it is undefined for the other trigger kinds. An empty value is treated as absent, and any other value is kept verbatim,
	/// a space included, because the LSP requires the character with that kind.
	/// </param>
	/// <param name="isRetrigger">
	/// <see langword="true"/> when signature help was already showing when it was triggered; retriggers occur when signature help is active
	/// and the user types a trigger character, moves the caret, or changes the document content.
	/// </param>
	/// <param name="activeSignatureHelp">
	/// The currently active signature help payload, when available. The value is carried independently of <paramref name="isRetrigger"/>:
	/// LSP defines it for retriggers, but a host may also supply it when it restores or re-requests signature help without a retrigger.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="triggerKind"/> is not a defined <see cref="SignatureHelpTriggerKind"/> value.
	/// </exception>
	public SignatureHelpContext(
		SignatureHelpTriggerKind triggerKind = SignatureHelpTriggerKind.Invoked,
		string? triggerCharacter = null,
		bool isRetrigger = false,
		SignatureHelpInformation? activeSignatureHelp = null
	)
	{
		if (!Enum.IsDefined(triggerKind))
			throw new ArgumentOutOfRangeException(nameof(triggerKind), triggerKind, "The trigger kind is not defined.");

		TriggerKind = triggerKind;

		TriggerCharacter =
			triggerKind == SignatureHelpTriggerKind.TriggerCharacter && !string.IsNullOrEmpty(triggerCharacter) ? triggerCharacter : null;

		IsRetrigger = isRetrigger;
		ActiveSignatureHelp = activeSignatureHelp;
	}

	/// <summary>The action that caused signature help to be triggered.</summary>
	public SignatureHelpTriggerKind TriggerKind { get; }

	/// <summary>
	/// The trigger character, or <see langword="null"/> when the trigger kind is not <see cref="SignatureHelpTriggerKind.TriggerCharacter"/>
	/// or no character was supplied.
	/// </summary>
	public string? TriggerCharacter { get; }

	/// <summary>Indicates whether signature help was already showing when it was triggered.</summary>
	public bool IsRetrigger { get; }

	/// <summary>The currently active signature help payload, when available.</summary>
	/// <remarks>
	/// The value is independent of <see cref="IsRetrigger"/>; see the constructor for details.
	/// </remarks>
	public SignatureHelpInformation? ActiveSignatureHelp { get; }
}
