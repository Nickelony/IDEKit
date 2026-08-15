using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Describes how items of one data type are projected into <see cref="TextDocumentSymbol"/> entries.
/// </summary>
/// <remarks>
/// <para>
/// The projection validates its own construction: <see cref="NameSelector"/> and
/// <see cref="KindSelector"/> are required and the remaining selectors are optional, so an
/// all-null selector bundle is not representable and the
/// <see cref="DocumentSymbolOutlineBuilder"/> methods do not re-check the bundle. The type is a
/// reference type, so a projection variable can still be <see langword="null"/>; the builder
/// rejects that with an <see cref="ArgumentNullException"/>.
/// </para>
/// <para>
/// The selectors are the only state, so the instances carry no identity of their own, and nothing in
/// the package mutates a projection after construction. Record equality compares the selector
/// delegates by delegate identity, not by their behavior: two projections built from different lambda
/// instances are unequal even when they select the same members, so equality is only meaningful for a
/// projection compared with itself or with a copy that shares the same delegate instances.
/// </para>
/// </remarks>
/// <typeparam name="TItem">The item data type.</typeparam>
public sealed record DocumentSymbolProjection<TItem>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentSymbolProjection{TItem}"/> record.
	/// </summary>
	/// <param name="nameSelector">Selects the display name of an item.</param>
	/// <param name="kindSelector">Selects the semantic category of an item.</param>
	/// <param name="dataSelector">Selects the optional host payload of an item.</param>
	/// <param name="rangeSelector">Selects the optional full range of an item.</param>
	/// <param name="selectionRangeSelector">Selects the optional name range of an item.</param>
	/// <param name="detailSelector">
	/// Selects the optional detail line of an item; blank values are treated as absent.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="nameSelector"/> or <paramref name="kindSelector"/> is <see langword="null"/>.
	/// </exception>
	public DocumentSymbolProjection(
		Func<TItem, string> nameSelector,
		Func<TItem, TextDocumentSymbolKind> kindSelector,
		Func<TItem, object?>? dataSelector = null,
		Func<TItem, TextRange?>? rangeSelector = null,
		Func<TItem, TextRange?>? selectionRangeSelector = null,
		Func<TItem, string?>? detailSelector = null)
	{
		ArgumentNullException.ThrowIfNull(nameSelector);
		ArgumentNullException.ThrowIfNull(kindSelector);

		NameSelector = nameSelector;
		KindSelector = kindSelector;
		DataSelector = dataSelector;
		RangeSelector = rangeSelector;
		SelectionRangeSelector = selectionRangeSelector;
		DetailSelector = detailSelector;
	}

	/// <summary>
	/// Gets the selector of the display name of an item.
	/// </summary>
	public Func<TItem, string> NameSelector { get; }

	/// <summary>
	/// Gets the selector of the semantic category of an item.
	/// </summary>
	public Func<TItem, TextDocumentSymbolKind> KindSelector { get; }

	/// <summary>
	/// Gets the selector of the optional host payload of an item, or <see langword="null"/> when the
	/// items carry no payload.
	/// </summary>
	public Func<TItem, object?>? DataSelector { get; }

	/// <summary>
	/// Gets the selector of the optional full range of an item, or <see langword="null"/> when the
	/// items carry no range.
	/// </summary>
	public Func<TItem, TextRange?>? RangeSelector { get; }

	/// <summary>
	/// Gets the selector of the optional name range of an item, or <see langword="null"/> when the
	/// items carry no name range.
	/// </summary>
	public Func<TItem, TextRange?>? SelectionRangeSelector { get; }

	/// <summary>
	/// Gets the selector of the optional detail line of an item, or <see langword="null"/> when the
	/// items carry no detail.
	/// </summary>
	public Func<TItem, string?>? DetailSelector { get; }
}
