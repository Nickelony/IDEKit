using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Represents one callable signature option within a signature-help payload.
/// </summary>
/// <remarks>
/// <para>
/// A payload can offer multiple signature options and a host can navigate between them; see
/// <see cref="TextSignatureHelp.WithActiveSignature"/>.
/// </para>
/// <para>
/// <see cref="Documentation"/> is plain text only; providers must flatten Markdown or other markup
/// before constructing the entry. Parameter labels are pre-resolved fragments of
/// <see cref="Label"/>: a label that is not a literal fragment of the signature label (for example
/// when the language server omits parameter text from the signature) cannot be located by a host
/// that highlights the active parameter. The label-search algorithm a host applies is documented in
/// <c>docs/EditorBindingGuide.md</c> (section 5.6).
/// </para>
/// <para>
/// The type uses reference equality. Its members include an immutable <see cref="Parameters"/> snapshot,
/// and a structural record comparison would compare that list by reference, so two information entries
/// with equal text would still report as different; reference equality states that honestly instead of
/// offering a partial value comparison. A host that must compare entries compares the members it cares
/// about.
/// </para>
/// </remarks>
public sealed class TextSignatureInformation
{
	private readonly TextSignatureActiveParameterSelection _activeParameter;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureInformation"/> class.
	/// </summary>
	/// <param name="label">The full signature label shown to the user.</param>
	/// <param name="documentation">
	/// Optional documentation shown beside or below the signature; blank values are treated as absent
	/// and other values are trimmed.
	/// </param>
	/// <param name="activeParameter">
	/// The active parameter selection for this signature. <see cref="TextSignatureActiveParameterSelection.NotSpecified"/>
	/// (the default) means the signature does not override the selection, so the payload-level selection
	/// applies instead. <see cref="TextSignatureActiveParameterSelection.None"/> means no parameter is active (for
	/// example an unmatched named argument), and a signature without parameters reports no active
	/// parameter regardless of the selection. An index at or beyond the parameter count falls back to
	/// the first parameter.
	/// </param>
	/// <param name="parameters">
	/// The parameters that compose the signature; the list must not contain
	/// <see langword="null"/> elements.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="label"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="label"/> is blank, or <paramref name="parameters"/> contains a
	/// <see langword="null"/> element.
	/// </exception>
	public TextSignatureInformation(
		string label,
		string? documentation = null,
		TextSignatureActiveParameterSelection activeParameter = default,
		IReadOnlyList<TextSignatureParameterInfo>? parameters = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(label);

		Label = label;
		Documentation = OptionalText.Normalize(documentation);
		Parameters = PayloadCollections.Capture(parameters, "parameters", nameof(parameters));

		_activeParameter = activeParameter;
	}

	/// <summary>
	/// Gets the full signature label shown to the user.
	/// </summary>
	public string Label { get; }

	/// <summary>
	/// Gets optional documentation for the signature.
	/// </summary>
	public string? Documentation { get; }

	/// <summary>
	/// Gets the normalized active parameter index for this signature, or <see langword="null"/> when
	/// the signature does not name one or has no parameters.
	/// </summary>
	/// <remarks>
	/// A specified value outside the parameter range falls back to the first parameter. The property
	/// ignores the payload-level selection; see <see cref="TextSignatureHelp.ActiveParameterIndex"/> for
	/// the effective active parameter of the payload.
	/// </remarks>
	public int? ActiveParameterIndex
		=> TextSignatureHelp.ResolveParameterIndex(_activeParameter, Parameters.Count);

	/// <summary>
	/// Gets the owned immutable snapshot of parameters that compose the signature.
	/// </summary>
	public IReadOnlyList<TextSignatureParameterInfo> Parameters { get; }

	/// <summary>
	/// Gets the active parameter selection this signature carries; the payload resolves it when
	/// <see cref="TextSignatureActiveParameterSelection.NotSpecified"/> allows the payload-level selection to
	/// apply instead.
	/// </summary>
	internal TextSignatureActiveParameterSelection ActiveParameterSelection => _activeParameter;
}
