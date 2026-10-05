using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Hover;

/// <summary>Represents hover content resolved for a symbol or document location.</summary>
/// <remarks>
/// <see cref="Content"/> must not be blank and is rendered
/// according to its markup kind; the remaining values are optional. <see cref="Range"/> uses zero-based UTF-16 offsets against the snapshot the hover
/// was requested for, and <see cref="DefinitionDiscriminator"/> carries the language-specific disambiguator a host passes to its definition request.
/// </remarks>
public sealed record HoverInformation
{
	private string? _symbolName;

	/// <param name="content">The hover text or markup supplied to the host.</param>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="content"/> is blank.</exception>
	public HoverInformation(MarkupContent content)
	{
		ArgumentNullException.ThrowIfNull(content);

		if (content.IsBlank)
			throw new ArgumentException("The hover content must not be blank.", nameof(content));

		Content = content;
	}

	/// <summary>The hover text or markup supplied to the host, with the markup kind that decides how it is rendered.</summary>
	public MarkupContent Content { get; }

	/// <summary>The optional symbol name; blank values are treated as absent and other values are trimmed.</summary>
	public string? SymbolName
	{
		get => _symbolName;
		init => _symbolName = OptionalTextNormalizer.Normalize(value);
	}

	/// <summary>The optional span the hover applies to, which a provider omits when it cannot identify one.</summary>
	public TextRange? Range { get; init; }

	/// <summary>
	/// The typed extension channel for a follow-up definition lookup, or <see langword="null"/> when the symbol has no navigation target.
	/// </summary>
	public DefinitionDiscriminator? DefinitionDiscriminator { get; init; }
}
