namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Projects flat or grouped item data into <see cref="TextDocumentSymbol"/> outline entries.
/// </summary>
/// <remarks>
/// <para>
/// The builder is document- and range-agnostic: it creates outline entries from a
/// <see cref="DocumentSymbolProjection{TItem}"/> and produces either a flat list
/// (<see cref="BuildFlatOutline{TItem}"/>) or one group root with its item children per group
/// (<see cref="BuildGroupedOutline{TGroup, TItem}"/>). Ranges, selection ranges, and the detail
/// line are projected when the corresponding selectors are supplied; deeper nesting than the one
/// grouping level is not produced, and callers that need it construct
/// <see cref="TextDocumentSymbol"/> instances directly.
/// </para>
/// <para>
/// The caller chooses every name and kind through the projections. Output and children follow the
/// order of the supplied groups and items. Returned lists are fresh caller-owned snapshots.
/// </para>
/// </remarks>
public static class DocumentSymbolOutlineBuilder
{
	/// <summary>
	/// Builds a flat outline from the given items.
	/// </summary>
	/// <typeparam name="TItem">The item data type.</typeparam>
	/// <param name="items">The item data to project.</param>
	/// <param name="projection">The projection applied to every item.</param>
	/// <returns>A new caller-owned list with one symbol per item and no children.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="items"/> or <paramref name="projection"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The projection's name selector returned <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The projection's kind selector returned a value that is not a defined
	/// <see cref="TextDocumentSymbolKind"/>; <see cref="TextDocumentSymbol"/> rejects it when the entry is
	/// constructed.
	/// </exception>
	public static IReadOnlyList<TextDocumentSymbol> BuildFlatOutline<TItem>(
		IReadOnlyList<TItem> items,
		DocumentSymbolProjection<TItem> projection)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(projection);

		var result = new List<TextDocumentSymbol>(items.Count);

		for (int i = 0; i < items.Count; i++)
			result.Add(CreateSymbol(items[i], projection, "an item", i));

		return result;
	}

	/// <summary>
	/// Builds a grouped outline with one group root and its item children per group.
	/// </summary>
	/// <typeparam name="TGroup">The group data type.</typeparam>
	/// <typeparam name="TItem">The item data type.</typeparam>
	/// <param name="groups">The group data to project.</param>
	/// <param name="groupProjection">The projection applied to every group root.</param>
	/// <param name="groupItemsSelector">
	/// Selects the item data of a group. The selector must not return <see langword="null"/>.
	/// </param>
	/// <param name="itemProjection">The projection applied to every item.</param>
	/// <returns>
	/// A new caller-owned list with one root per group, carrying the group's projected items as
	/// children.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="groups"/>, <paramref name="groupProjection"/>,
	/// <paramref name="groupItemsSelector"/>, or <paramref name="itemProjection"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="groupItemsSelector"/> returned <see langword="null"/> for a group, or a projection's
	/// name selector returned <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// A projection's kind selector returned a value that is not a defined
	/// <see cref="TextDocumentSymbolKind"/>; <see cref="TextDocumentSymbol"/> rejects it when the entry is
	/// constructed.
	/// </exception>
	public static IReadOnlyList<TextDocumentSymbol> BuildGroupedOutline<TGroup, TItem>(
		IReadOnlyList<TGroup> groups,
		DocumentSymbolProjection<TGroup> groupProjection,
		Func<TGroup, IReadOnlyList<TItem>> groupItemsSelector,
		DocumentSymbolProjection<TItem> itemProjection)
	{
		ArgumentNullException.ThrowIfNull(groups);
		ArgumentNullException.ThrowIfNull(groupProjection);
		ArgumentNullException.ThrowIfNull(groupItemsSelector);
		ArgumentNullException.ThrowIfNull(itemProjection);

		var result = new List<TextDocumentSymbol>(groups.Count);

		for (int i = 0; i < groups.Count; i++)
		{
			TGroup group = groups[i];
			IReadOnlyList<TItem> items = groupItemsSelector(group);

			if (items is null)
			{
				throw new InvalidOperationException(
					$"The items selector returned null for the group at index {i}.");
			}

			var children = new List<TextDocumentSymbol>(items.Count);

			for (int j = 0; j < items.Count; j++)
				children.Add(CreateSymbol(items[j], itemProjection, "an item", j, groupIndex: i));

			result.Add(CreateSymbol(group, groupProjection, "a group", i, children: children));
		}

		return result;
	}

	private static TextDocumentSymbol CreateSymbol<TItem>(
		TItem item,
		DocumentSymbolProjection<TItem> projection,
		string subject,
		int index,
		int? groupIndex = null,
		IReadOnlyList<TextDocumentSymbol>? children = null)
	{
		string name = projection.NameSelector(item);

		if (name is null)
		{
			throw new InvalidOperationException(
				groupIndex is int group
					? $"The projection name selector returned null for {subject} at index {index} of the group at index {group}."
					: $"The projection name selector returned null for {subject} at index {index}.");
		}

		return new(
			name,
			projection.KindSelector(item),
			projection.RangeSelector?.Invoke(item),
			projection.SelectionRangeSelector?.Invoke(item))
		{
			Detail = projection.DetailSelector?.Invoke(item),
			Children = children,
			Data = projection.DataSelector?.Invoke(item)
		};
	}
}
