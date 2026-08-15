using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.IDEKit.IntelliSense.Hover;

/// <summary>
/// Represents hover content resolved for a symbol or document location.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Content"/> must not be blank; the remaining values are optional and assigned with
/// an object initializer. Hosts render <see cref="Content"/> according to
/// <see cref="ContentKind"/> and may ignore any optional value.
/// </para>
/// <para>
/// <see cref="Range"/> identifies the hovered span using zero-based UTF-16 document offsets
/// against the snapshot the hover was requested for, so a host can apply it without coordinate
/// conversion. Providers may omit it when they cannot identify a span.
/// </para>
/// <para>
/// <see cref="DefinitionDiscriminator"/> is the single typed extension channel for a follow-up
/// definition lookup: a hover provider that recognizes a navigation target supplies its
/// language-specific discriminator, and a host that navigates from the hover passes it to
/// <see cref="TextDefinitionRequest"/> unchanged. A host that does not recognize a discriminator
/// ignores it.
/// </para>
/// </remarks>
public sealed record TextHoverInfo
{
	private string? _symbolName;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextHoverInfo"/> record. Optional values are
	/// assigned with an object initializer.
	/// </summary>
	/// <param name="content">The hover text or markup supplied to the host.</param>
	/// <param name="contentKind">The format used by <paramref name="content"/>.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="content"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="contentKind"/> is not a defined <see cref="TextMarkupKind"/> value.
	/// </exception>
	public TextHoverInfo(
		string content,
		TextMarkupKind contentKind = TextMarkupKind.PlainText)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(content);

		if (!Enum.IsDefined(contentKind))
			throw new ArgumentOutOfRangeException(nameof(contentKind), contentKind, "The markup kind is not defined.");

		Content = content;
		ContentKind = contentKind;
	}

	/// <summary>
	/// Gets the hover text or markup supplied to the host.
	/// </summary>
	public string Content { get; }

	/// <summary>
	/// Gets the format used by <see cref="Content"/>.
	/// </summary>
	public TextMarkupKind ContentKind { get; }

	/// <summary>
	/// Gets the optional symbol name associated with the hover content, assigned with an object
	/// initializer; blank values are treated as absent and other values are trimmed.
	/// </summary>
	public string? SymbolName
	{
		get => _symbolName;
		init => _symbolName = OptionalText.Normalize(value);
	}

	/// <summary>
	/// Gets the optional span the hover applies to, as zero-based UTF-16 document offsets against
	/// the snapshot the hover was requested for, assigned with an object initializer.
	/// </summary>
	public TextRange? Range { get; init; }

	/// <summary>
	/// Gets the optional language-specific discriminator that identifies the navigation target of the
	/// hovered symbol, assigned with an object initializer; see the type remarks.
	/// </summary>
	public TextDefinitionDiscriminator? DefinitionDiscriminator { get; init; }
}
