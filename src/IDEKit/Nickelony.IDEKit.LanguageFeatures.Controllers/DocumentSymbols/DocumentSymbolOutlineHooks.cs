using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>Groups the selectors through which <see cref="DocumentSymbolOutlineBuilder"/> reads items of one data type.</summary>
/// <typeparam name="TItem">The item data type.</typeparam>
/// <remarks>
/// The name and kind selectors are required; the remaining selectors are optional and a <see langword="null"/> one is treated as absent.
/// The builder rejects hooks whose required selector is <see langword="null"/>.
/// </remarks>
public sealed class DocumentSymbolOutlineHooks<TItem>
{
	/// <summary>The selector of the display name of an item.</summary>
	public required Func<TItem, string> NameSelector { get; init; }

	/// <summary>The selector of the semantic category of an item.</summary>
	public required Func<TItem, DocumentSymbolKind> KindSelector { get; init; }

	/// <summary>The selector of the optional host payload of an item, or <see langword="null"/> when the items carry no payload.</summary>
	public Func<TItem, object?>? DataSelector { get; init; }

	/// <summary>The selector of the optional full range of an item, or <see langword="null"/> when the items carry no range.</summary>
	public Func<TItem, TextRange?>? RangeSelector { get; init; }

	/// <summary>The selector of the optional name range of an item, or <see langword="null"/> when the items carry no name range.</summary>
	public Func<TItem, TextRange?>? SelectionRangeSelector { get; init; }

	/// <summary>
	/// The selector of the optional detail line of an item, or <see langword="null"/> when the items carry no detail; blank values are treated
	/// as absent.
	/// </summary>
	public Func<TItem, string?>? DetailSelector { get; init; }
}
