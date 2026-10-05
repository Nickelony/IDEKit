using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>Represents the signature-help payload for a callable item: the available signature options and the active selection.</summary>
/// <remarks>
/// The payload mirrors the LSP signature-help model and carries enough for a host to offer "1 of N" overload navigation
/// without another provider round-trip; see <see cref="WithActiveSignature"/>. <see cref="ActiveParameterIndex"/> applies the LSP precedence
/// and default rules. Uses reference equality, so a caller compares the members it needs.
/// </remarks>
public sealed class SignatureHelpInformation
{
	private readonly ReadOnlyCollection<SignatureInformation> _signatures;
	private readonly SignatureActiveParameterSelection _activeParameter;

	/// <param name="signatures">
	/// The signature options; at least one entry is required because a payload without signatures has no content to present
	/// (providers return <see langword="null"/> instead). Elements must not be <see langword="null"/>.
	/// </param>
	/// <param name="activeSignatureIndex">
	/// The zero-based active signature index. A value outside the range of <paramref name="signatures"/> falls back to zero,
	/// which is the LSP default.
	/// </param>
	/// <param name="activeParameter">
	/// The payload-level active
	/// parameter selection for the active signature. <see cref="SignatureActiveParameterSelection.NotSpecified"/> (the default) means the payload
	/// does not override the active signature's own selection; when the active signature does not name one either, the first parameter
	/// is selected for a signature that has parameters. <see cref="SignatureActiveParameterSelection.None"/> means no parameter is active
	/// (for example an unmatched named argument). An index at or beyond the active signature's parameter range falls back to the first parameter,
	/// which is the protocol default; a signature without parameters has no active parameter regardless of the selection.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="signatures"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="signatures"/> is empty or contains a <see langword="null"/> element.</exception>
	public SignatureHelpInformation(
		IReadOnlyList<SignatureInformation> signatures,
		int activeSignatureIndex = 0,
		SignatureActiveParameterSelection activeParameter = default
	)
	{
		ArgumentNullException.ThrowIfNull(signatures);

		if (signatures.Count == 0)
		{
			throw new ArgumentException(
				"At least one signature is required; return null instead when no signature is available.",
				nameof(signatures)
			);
		}

		// CollectionSnapshots.Capture throws the ArgumentException documented above for a null element.
		_signatures = CollectionSnapshots.Capture(signatures, "signatures", nameof(signatures));
		ActiveSignatureIndex = NormalizeActiveSignatureIndex(activeSignatureIndex, _signatures.Count);
		_activeParameter = activeParameter;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="SignatureHelpInformation"/> class from an owned signature snapshot. Used internally to change
	/// the active signature without copying the array again.
	/// </summary>
	private SignatureHelpInformation(
		ReadOnlyCollection<SignatureInformation> signatures,
		int activeSignatureIndex,
		SignatureActiveParameterSelection activeParameter
	)
	{
		_signatures = signatures;
		ActiveSignatureIndex = activeSignatureIndex;
		_activeParameter = activeParameter;
	}

	/// <summary>The owned immutable snapshot of signature options.</summary>
	public IReadOnlyList<SignatureInformation> Signatures => _signatures;

	/// <summary>The zero-based index of the active signature within <see cref="Signatures"/>.</summary>
	public int ActiveSignatureIndex { get; }

	public SignatureInformation ActiveSignature => Signatures[ActiveSignatureIndex];

	/// <summary>
	/// The effective zero-based active parameter index for <see cref="ActiveSignature"/>, or <see langword="null"/> when no parameter is active
	/// or the active signature has no parameters.
	/// </summary>
	/// <remarks>
	/// The value is never a sentinel: an index into the active signature's parameters or <see langword="null"/>. Resolution follows the LSP
	/// precedence and default rules documented on the constructor's <c>activeParameter</c> parameter.
	/// </remarks>
	public int? ActiveParameterIndex
	{
		get
		{
			SignatureInformation activeSignature = ActiveSignature;
			SignatureActiveParameterSelection selection = activeSignature.ActiveParameterSelection;

			return selection.IsSpecified || selection.IsNone
				? activeSignature.ActiveParameterIndex
				: ResolvePayloadActiveParameterIndex(_activeParameter, activeSignature.Parameters);
		}
	}

	/// <summary>
	/// Returns a payload with the same signatures and payload-level selection but a different active signature, which a host uses
	/// for overload navigation.
	/// </summary>
	/// <param name="activeSignatureIndex">
	/// The zero-based active signature index. A value outside the range of <see cref="Signatures"/> falls back to zero, which is the LSP default.
	/// </param>
	/// <returns>The new payload; the active parameter index is resolved again for the selected signature using the same rules.</returns>
	public SignatureHelpInformation WithActiveSignature(int activeSignatureIndex) =>
		new(_signatures, NormalizeActiveSignatureIndex(activeSignatureIndex, _signatures.Count), _activeParameter);

	private static int NormalizeActiveSignatureIndex(int activeSignatureIndex, int signatureCount) =>
		activeSignatureIndex >= 0 && activeSignatureIndex < signatureCount ? activeSignatureIndex : 0;

	private static int? ResolvePayloadActiveParameterIndex(
		SignatureActiveParameterSelection activeParameter,
		IReadOnlyList<ParameterInformation> parameters
	)
	{
		if (parameters.Count == 0 || activeParameter.IsNone)
			return null;

		return ResolveParameterIndex(activeParameter, parameters.Count) ?? 0;
	}

	/// <summary>
	/// Normalizes a parameter index into the range of a signature's parameters; a value outside the range falls back to the first parameter,
	/// which is the LSP default.
	/// </summary>
	internal static int NormalizeParameterIndex(int index, int parameterCount) => index >= 0 && index < parameterCount ? index : 0;

	/// <summary>
	/// Resolves a parameter
	/// selection that names an index into a signature's parameter range; an out-of-range value falls back to the first parameter, the LSP default,
	/// and <see cref="SignatureActiveParameterSelection.NotSpecified"/> or <see cref="SignatureActiveParameterSelection.None"/> reports
	/// no active parameter.
	/// </summary>
	/// <param name="selection">The parameter selection to resolve.</param>
	/// <param name="parameterCount">The number of parameters the selection applies to.</param>
	/// <returns>The normalized zero-based index, or <see langword="null"/> when the selection names none.</returns>
	internal static int? ResolveParameterIndex(SignatureActiveParameterSelection selection, int parameterCount) =>
		selection.Index is int index && parameterCount > 0 ? NormalizeParameterIndex(index, parameterCount) : null;
}
