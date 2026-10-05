using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>Represents one callable signature option within a signature-help payload.</summary>
/// <remarks>
/// A host navigates the payload's signature options through <see cref="SignatureHelpInformation.WithActiveSignature"/>.
/// A parameter label that is not a literal fragment of <see cref="Label"/> cannot be located by a host that highlights the active parameter;
/// see the
/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorBindingGuide.md#66-signature-parameter-highlight">editor-binding guide</see>.
/// Uses reference equality, so a caller compares the members it needs.
/// </remarks>
public sealed class SignatureInformation
{
	/// <param name="label">The full signature label shown to the user.</param>
	/// <param name="documentation">
	/// Optional documentation shown beside or below the signature, with its markup kind; blank content is treated as absent.
	/// </param>
	/// <param name="activeParameter">
	/// The active parameter selection for this signature.
	/// <see cref="SignatureActiveParameterSelection.NotSpecified"/> (the default) means the signature does not override the selection,
	/// so the payload-level selection applies instead. <see cref="SignatureActiveParameterSelection.None"/> means no parameter is active
	/// (for example an unmatched named argument), and a signature without parameters reports no active parameter regardless of the selection.
	/// An index at or beyond the parameter count falls back to the first parameter.
	/// </param>
	/// <param name="parameters">The parameters that compose the signature; the list must not contain <see langword="null"/> elements.</param>
	/// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="label"/> is blank, or <paramref name="parameters"/> contains a <see langword="null"/> element.
	/// </exception>
	public SignatureInformation(
		string label,
		MarkupContent? documentation = null,
		SignatureActiveParameterSelection activeParameter = default,
		IReadOnlyList<ParameterInformation>? parameters = null
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(label);

		Label = label;
		Documentation = documentation is { IsBlank: true } ? null : documentation;
		Parameters = CollectionSnapshots.Capture(parameters, "parameters", nameof(parameters));

		ActiveParameterSelection = activeParameter;
	}

	/// <summary>The full signature label shown to the user.</summary>
	public string Label { get; }

	/// <summary>The optional documentation shown beside or below the signature, or <see langword="null"/> when there is none.</summary>
	public MarkupContent? Documentation { get; }

	/// <summary>
	/// Returns the normalized active parameter index for this signature, or <see langword="null"/> when the signature does not name
	/// one or has no parameters.
	/// </summary>
	/// <remarks>
	/// A specified value outside the parameter range falls back to the first parameter. The property ignores the payload-level selection;
	/// see <see cref="SignatureHelpInformation.ActiveParameterIndex"/> for the effective active parameter of the payload.
	/// </remarks>
	public int? ActiveParameterIndex => SignatureHelpInformation.ResolveParameterIndex(ActiveParameterSelection, Parameters.Count);

	/// <summary>The owned immutable snapshot of parameters that compose the signature.</summary>
	public IReadOnlyList<ParameterInformation> Parameters { get; }

	/// <summary>
	/// The active parameter selection this signature carries. The payload uses its own selection
	/// instead when this one is <see cref="SignatureActiveParameterSelection.NotSpecified"/>.
	/// </summary>
	internal SignatureActiveParameterSelection ActiveParameterSelection { get; }
}
