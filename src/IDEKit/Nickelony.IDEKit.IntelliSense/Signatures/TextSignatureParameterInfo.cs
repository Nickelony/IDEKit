using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Represents one parameter entry within a signature-help payload.
/// </summary>
/// <remarks>
/// <para>
/// Providers supply pre-resolved labels: when the label could be extracted from the signature
/// label it is a plain-text fragment of that label, and when it could not be resolved it is empty.
/// </para>
/// <para>
/// The type uses reference equality. A parameter entry is a payload fragment rather than a value:
/// structural equality would invite a comparison that cannot be completed, because the surrounding
/// <see cref="TextSignatureInformation"/> compares its parameter list by reference. A host that must
/// compare entries compares the members it cares about.
/// </para>
/// </remarks>
public sealed class TextSignatureParameterInfo
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureParameterInfo"/> class.
	/// </summary>
	/// <param name="label">
	/// The parameter label shown in the signature UI, or an empty value when the provider could not
	/// resolve one. A blank label is never locatable inside the signature label, so a host that
	/// highlights the active parameter must treat it as unresolved and present the signature
	/// without a highlight (see <c>docs/EditorBindingGuide.md</c>, section 5.6).
	/// </param>
	/// <param name="documentation">
	/// Optional parameter documentation; blank values are treated as absent and other values are trimmed.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="label"/> is <see langword="null"/>.
	/// </exception>
	public TextSignatureParameterInfo(string label, string? documentation = null)
	{
		ArgumentNullException.ThrowIfNull(label);

		Label = label;
		Documentation = OptionalText.Normalize(documentation);
	}

	/// <summary>
	/// Gets the parameter label shown in the signature UI.
	/// </summary>
	public string Label { get; }

	/// <summary>
	/// Gets optional documentation for the parameter.
	/// </summary>
	public string? Documentation { get; }
}
