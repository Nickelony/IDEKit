using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>Projects flat or grouped item data into <see cref="DocumentSymbol"/> outline entries.</summary>
/// <remarks>
/// The flat overload projects each item with no children, and the grouped overload adds one root per group, projecting groups and items
/// with their own <see cref="DocumentSymbolOutlineHooks{TItem}"/>. Nesting deeper than one group is not produced, and output and children follow
/// the input order; returned lists are caller-owned snapshots.
/// </remarks>
public static class DocumentSymbolOutlineBuilder
{
	/// <summary>Builds a flat outline from the given items.</summary>
	/// <typeparam name="TItem">The item data type.</typeparam>
	/// <param name="items">The item data to project.</param>
	/// <param name="hooks">The selectors applied to every item.</param>
	/// <returns>A new caller-owned list with one symbol per item and no children.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="hooks"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The name selector returned <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="hooks"/> supplies no name selector or no kind selector, or the name selector returned a blank name;
	/// <see cref="DocumentSymbol"/> rejects the name when the entry is constructed.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The projection's kind selector returned a value that is not a defined <see cref="DocumentSymbolKind"/>;
	/// <see cref="DocumentSymbol"/> rejects it when the entry is constructed.
	/// </exception>
	public static IReadOnlyList<DocumentSymbol> Build<TItem>(IReadOnlyList<TItem> items, DocumentSymbolOutlineHooks<TItem> hooks)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(hooks);

		ValidateHooks(hooks, nameof(hooks));

		var result = new List<DocumentSymbol>(items.Count);

		for (int i = 0; i < items.Count; i++)
			result.Add(CreateSymbol(item: items[i], hooks, subject: "an item", index: i));

		return result;
	}

	/// <summary>Builds a grouped outline with one group root and its item children per group.</summary>
	/// <typeparam name="TGroup">The group data type.</typeparam>
	/// <typeparam name="TItem">The item data type.</typeparam>
	/// <param name="groups">The group data to project.</param>
	/// <param name="groupHooks">The selectors applied to every group root.</param>
	/// <param name="groupItemsSelector">Selects the item data of a group; a <see langword="null"/> return is rejected.</param>
	/// <param name="itemHooks">The selectors applied to every item.</param>
	/// <returns>A new caller-owned list with one root per group, carrying the group's projected items as children.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="groups"/>, <paramref name="groupHooks"/>, <paramref name="groupItemsSelector"/>,
	/// or <paramref name="itemHooks"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="groupItemsSelector"/> returned <see langword="null"/> for a group, or a projection's name selector
	/// returned <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// A projection's name selector returned a blank name; <see cref="DocumentSymbol"/> rejects it when the entry is constructed.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// A projection's kind selector returned a value that is not a defined <see cref="DocumentSymbolKind"/>;
	/// <see cref="DocumentSymbol"/> rejects it when the entry is constructed.
	/// </exception>
	public static IReadOnlyList<DocumentSymbol> Build<TGroup, TItem>(
		IReadOnlyList<TGroup> groups,
		DocumentSymbolOutlineHooks<TGroup> groupHooks,
		Func<TGroup, IReadOnlyList<TItem>> groupItemsSelector,
		DocumentSymbolOutlineHooks<TItem> itemHooks
	)
	{
		ArgumentNullException.ThrowIfNull(groups);
		ArgumentNullException.ThrowIfNull(groupHooks);
		ArgumentNullException.ThrowIfNull(groupItemsSelector);
		ArgumentNullException.ThrowIfNull(itemHooks);

		ValidateHooks(groupHooks, nameof(groupHooks));
		ValidateHooks(itemHooks, nameof(itemHooks));

		var result = new List<DocumentSymbol>(groups.Count);

		for (int i = 0; i < groups.Count; i++)
		{
			TGroup group = groups[i];

			IReadOnlyList<TItem> items =
				groupItemsSelector(group) ?? throw new InvalidOperationException($"The items selector returned null for the group at index {i}.");

			var children = new List<DocumentSymbol>(items.Count);

			for (int j = 0; j < items.Count; j++)
				children.Add(CreateSymbol(item: items[j], hooks: itemHooks, subject: "an item", index: j, groupIndex: i));

			result.Add(CreateSymbol(item: group, hooks: groupHooks, subject: "a group", index: i, children: children));
		}

		return result;
	}

	private static void ValidateHooks<TItem>(DocumentSymbolOutlineHooks<TItem> hooks, string parameterName)
	{
		if (hooks.NameSelector is null || hooks.KindSelector is null)
			throw new ArgumentException("The hooks must supply a name selector and a kind selector.", parameterName);
	}

	private static DocumentSymbol CreateSymbol<TItem>(
		TItem item,
		DocumentSymbolOutlineHooks<TItem> hooks,
		string subject,
		int index,
		int? groupIndex = null,
		IReadOnlyList<DocumentSymbol>? children = null
	)
	{
		string name =
			hooks.NameSelector(item)
			?? throw new InvalidOperationException(
				groupIndex is int group
					? $"The projection name selector returned null for {subject} at index {index} of the group at index {group}."
					: $"The projection name selector returned null for {subject} at index {index}."
			);

		return new(
			name,
			kind: hooks.KindSelector(item),
			range: hooks.RangeSelector?.Invoke(item),
			selectionRange: hooks.SelectionRangeSelector?.Invoke(item)
		)
		{
			Detail = hooks.DetailSelector?.Invoke(item),
			Children = children,
			Data = hooks.DataSelector?.Invoke(item),
		};
	}
}
