using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>Represents one parameter entry within a signature-help payload.</summary>
/// <remarks>
/// Labels are pre-resolved by the provider: an extracted label is a plain-text fragment of the signature label and an unresolved one is empty,
/// while an offset-form label carries <see cref="Range"/> for unambiguous highlighting. The type uses reference equality, so a caller compares
/// the members it needs.
/// </remarks>
public sealed class ParameterInformation
{
	/// <param name="label">
	/// The parameter label shown
	/// in the signature UI, or an empty value when the provider could not resolve one. A blank label is never locatable inside the signature label,
	/// so a host that highlights the active parameter must treat it as unresolved and present the signature without a highlight (see the
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorBindingGuide.md#66-signature-parameter-highlight">editor-binding guide</see>).
	/// </param>
	/// <param name="documentation">Optional parameter documentation with its markup kind; blank content is treated as absent.</param>
	/// <param name="range">
	/// The exact range of the parameter within its signature label, or <see langword="null"/> when the provider supplied only a plain label.
	/// When the range is present a host highlights the active parameter by this offset range; when it is absent the label is located by search.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
	public ParameterInformation(string label, MarkupContent? documentation = null, TextRange? range = null)
	{
		ArgumentNullException.ThrowIfNull(label);

		Label = label;
		Documentation = documentation is { IsBlank: true } ? null : documentation;
		Range = range;
	}

	/// <summary>The parameter label shown in the signature UI.</summary>
	public string Label { get; }

	/// <summary>The optional parameter documentation, or <see langword="null"/> when there is none.</summary>
	public MarkupContent? Documentation { get; }

	/// <summary>
	/// The exact range of the parameter within its signature label, or <see langword="null"/> when the provider supplied only a plain label.
	/// </summary>
	/// <remarks>
	/// When the range is present a host highlights the active
	/// parameter by this offset range. When it is absent, the label is a literal fragment of the signature label and a host locates it by search;
	/// see the
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorBindingGuide.md#66-signature-parameter-highlight">editor-binding guide</see>.
	/// </remarks>
	public TextRange? Range { get; }
}
