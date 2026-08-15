using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Represents the signature-help payload for a callable item: the available signature options and
/// the active selection.
/// </summary>
/// <remarks>
/// <para>
/// The payload mirrors the LSP signature-help model. It carries one or more signatures, the active
/// signature index, and the active parameter selection for the active signature, so a host can offer
/// "1 of N" overload navigation without another provider round-trip; see
/// <see cref="WithActiveSignature"/>.
/// </para>
/// <para>
/// <see cref="ActiveParameterIndex"/> resolves the effective active parameter with the LSP
/// precedence and default rules; see that property for the full resolution details.
/// </para>
/// <para>
/// The type uses reference equality. Its payload includes an immutable <see cref="Signatures"/> snapshot,
/// and a structural record comparison would compare that list by reference, so two payloads with equal
/// text would still report as different; reference equality states that honestly instead of offering a
/// partial value comparison. A host that must compare payloads compares the members it cares about.
/// </para>
/// </remarks>
public sealed class TextSignatureHelp
{
	private readonly ReadOnlyCollection<TextSignatureInformation> _signatures;
	private readonly TextSignatureActiveParameterSelection _activeParameter;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelp"/> class.
	/// </summary>
	/// <param name="signatures">
	/// The signature options; at least one entry is required because a payload without signatures
	/// has no content to present (providers return <see langword="null"/> instead). Elements must
	/// not be <see langword="null"/>.
	/// </param>
	/// <param name="activeSignatureIndex">
	/// The zero-based active signature index. A value outside the range of
	/// <paramref name="signatures"/> falls back to zero, which is the LSP default.
	/// </param>
	/// <param name="activeParameter">
	/// The payload-level active parameter selection for the active signature.
	/// <see cref="TextSignatureActiveParameterSelection.NotSpecified"/> (the default) means the payload does not
	/// override the active signature's own selection; when the active signature does not name one
	/// either, the first parameter is selected for a signature that has parameters.
	/// <see cref="TextSignatureActiveParameterSelection.None"/> means no parameter is active (for example an
	/// unmatched named argument). An index at or beyond the active signature's parameter range falls
	/// back to the first parameter, which is the protocol default; a signature without parameters has
	/// no active parameter regardless of the selection.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="signatures"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="signatures"/> is empty or contains a <see langword="null"/> element.
	/// </exception>
	public TextSignatureHelp(
		IReadOnlyList<TextSignatureInformation> signatures,
		int activeSignatureIndex = 0,
		TextSignatureActiveParameterSelection activeParameter = default)
	{
		ArgumentNullException.ThrowIfNull(signatures);

		if (signatures.Count == 0)
		{
			throw new ArgumentException(
				"At least one signature is required; return null instead when no signature is available.",
				nameof(signatures));
		}

		// PayloadCollections.Capture rejects a null element with the same exception and message.
		_signatures = PayloadCollections.Capture(signatures, "signatures", nameof(signatures));
		ActiveSignatureIndex = NormalizeActiveSignatureIndex(activeSignatureIndex, _signatures.Count);
		_activeParameter = activeParameter;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelp"/> class from an owned
	/// signature snapshot. Used internally to change the active signature without copying the array
	/// again.
	/// </summary>
	private TextSignatureHelp(
		ReadOnlyCollection<TextSignatureInformation> signatures,
		int activeSignatureIndex,
		TextSignatureActiveParameterSelection activeParameter)
	{
		_signatures = signatures;
		ActiveSignatureIndex = activeSignatureIndex;
		_activeParameter = activeParameter;
	}

	/// <summary>
	/// Gets the owned immutable snapshot of signature options.
	/// </summary>
	public IReadOnlyList<TextSignatureInformation> Signatures => _signatures;

	/// <summary>
	/// Gets the zero-based index of the active signature within <see cref="Signatures"/>.
	/// </summary>
	public int ActiveSignatureIndex { get; }

	/// <summary>
	/// Gets the active signature option.
	/// </summary>
	public TextSignatureInformation ActiveSignature => Signatures[ActiveSignatureIndex];

	/// <summary>
	/// Gets the effective zero-based active parameter index for <see cref="ActiveSignature"/>, or
	/// <see langword="null"/> when no parameter is active or the active signature has no parameters.
	/// </summary>
	/// <remarks>
	/// The value is never a sentinel: it is either an index into the active signature's parameters
	/// or <see langword="null"/>. Resolution follows the LSP precedence and default rules documented
	/// on the constructor's <c>activeParameter</c> parameter: the active signature's own selection
	/// wins when it names one (an explicit <see cref="TextSignatureActiveParameterSelection.None"/> reports no
	/// active parameter); otherwise, the payload-level selection applies, where
	/// <see cref="TextSignatureActiveParameterSelection.NotSpecified"/> and an out-of-range index select the
	/// first parameter and <see cref="TextSignatureActiveParameterSelection.None"/> reports none; and a
	/// signature without parameters always reports <see langword="null"/> (the LSP 3.18
	/// "no active parameter" state).
	/// </remarks>
	public int? ActiveParameterIndex
	{
		get
		{
			TextSignatureInformation activeSignature = ActiveSignature;
			TextSignatureActiveParameterSelection selection = activeSignature.ActiveParameterSelection;

			return selection.IsSpecified || selection.IsNone
				? activeSignature.ActiveParameterIndex
				: ResolvePayloadActiveParameterIndex(_activeParameter, activeSignature.Parameters);
		}
	}

	/// <summary>
	/// Returns a payload with the same signatures and payload-level selection but a different
	/// active signature, which a host uses for overload navigation.
	/// </summary>
	/// <param name="activeSignatureIndex">
	/// The zero-based active signature index. A value outside the range of <see cref="Signatures"/> falls
	/// back to zero, which is the LSP default.
	/// </param>
	/// <returns>
	/// The new payload; the active parameter index is resolved again for the selected signature
	/// using the same rules.
	/// </returns>
	public TextSignatureHelp WithActiveSignature(int activeSignatureIndex)
		=> new(_signatures, NormalizeActiveSignatureIndex(activeSignatureIndex, _signatures.Count), _activeParameter);

	private static int NormalizeActiveSignatureIndex(int activeSignatureIndex, int signatureCount)
		=> activeSignatureIndex >= 0 && activeSignatureIndex < signatureCount ? activeSignatureIndex : 0;

	private static int? ResolvePayloadActiveParameterIndex(
		TextSignatureActiveParameterSelection activeParameter,
		IReadOnlyList<TextSignatureParameterInfo> parameters)
	{
		if (parameters.Count == 0 || activeParameter.IsNone)
			return null;

		return ResolveParameterIndex(activeParameter, parameters.Count) ?? 0;
	}

	/// <summary>
	/// Normalizes a parameter index into the range of a signature's parameters; a value outside the
	/// range falls back to the first parameter, which is the LSP default.
	/// </summary>
	internal static int NormalizeParameterIndex(int index, int parameterCount)
		=> index >= 0 && index < parameterCount ? index : 0;

	/// <summary>
	/// Resolves a parameter selection that names an index against a parameter count: the index is
	/// normalized into range (a value outside the range falls back to the first parameter, which is the
	/// LSP default), and a selection that names no index - <see cref="TextSignatureActiveParameterSelection.NotSpecified"/> or
	/// <see cref="TextSignatureActiveParameterSelection.None"/> - reports no active parameter. Every active-parameter
	/// read resolves through this rule, so the signature-level property and the payload-level fallback use
	/// one precedence policy.
	/// </summary>
	/// <param name="selection">The parameter selection to resolve.</param>
	/// <param name="parameterCount">The number of parameters the selection applies to.</param>
	/// <returns>The normalized zero-based index, or <see langword="null"/> when the selection names none.</returns>
	internal static int? ResolveParameterIndex(TextSignatureActiveParameterSelection selection, int parameterCount)
		=> selection.Index is int index && parameterCount > 0
			? NormalizeParameterIndex(index, parameterCount)
			: null;
}
