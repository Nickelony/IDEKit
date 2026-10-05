using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Describes how a host should update the completion session after processing input.</summary>
/// <remarks>
/// A decision opens, refreshes, closes, or leaves the completion session unchanged; a decision may request a close and carry a replacement
/// at the same time, and a host applies the close first. An opening decision carries at least one item and a replacement range, and a decision
/// without items carries no replacement range. Value equality compares <see cref="Items"/> by list reference, so two decisions are equal
/// only when they share one list instance, and <see cref="Open"/> captures an owned copy of its items.
/// </remarks>
public readonly record struct CompletionSessionDirective
{
	/// <summary>Initializes a decision in one of the documented states.</summary>
	/// <param name="shouldClose">Whether an active completion session should be dismissed.</param>
	/// <param name="items">
	/// The items to show when opening or refreshing a completion session; at least one item is required when the list is supplied.
	/// </param>
	/// <param name="replacementRange">
	/// The zero-based replacement range to apply when the decision opens or refreshes a session; required when <paramref name="items"/> is supplied,
	/// and <see langword="null"/> otherwise.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The argument combination violates the state invariant: items without a replacement range, a replacement range without items, or an empty
	/// items list.
	/// </exception>
	/// <remarks>
	/// Use <see cref="None"/>, <see cref="Close"/>, <see cref="Open"/>, or <see cref="NoMatches"/> to create a state explicitly; adapters that build
	/// a decision from state data use this constructor, which enforces the same state invariant.
	/// </remarks>
	public CompletionSessionDirective(bool shouldClose, IReadOnlyList<CompletionItem>? items, TextRange? replacementRange = null)
		: this(shouldClose, items, replacementRange, allItemsFilteredOut: false)
	{
		ValidateState(items, replacementRange, itemsParameterName: nameof(items), replacementRangeParameterName: nameof(replacementRange));
	}

	private CompletionSessionDirective(bool shouldClose, IReadOnlyList<CompletionItem>? items, TextRange? replacementRange, bool allItemsFilteredOut)
	{
		ShouldClose = shouldClose;
		Items = items;
		ReplacementRange = replacementRange;
		AllItemsFilteredOut = allItemsFilteredOut;
	}

	public bool ShouldClose { get; init; }

	/// <summary>
	/// The items to show when opening or refreshing a completion session, or <see langword="null"/> when the decision does not open one.
	/// </summary>
	public IReadOnlyList<CompletionItem>? Items { get; }

	/// <summary>
	/// The zero-based replacement range to apply when opening or refreshing a completion session, or <see langword="null"/> when the decision
	/// does not open one.
	/// </summary>
	public TextRange? ReplacementRange { get; }

	/// <summary>Whether the decision reports that the provider returned candidates that the current word filtered out completely.</summary>
	/// <remarks>
	/// The state carries no items and no replacement range. It lets a host dismiss a session that narrowed to nothing while leaving a session
	/// with no candidates unchanged (<see cref="None"/>).
	/// </remarks>
	public bool AllItemsFilteredOut { get; }

	/// <summary>
	/// Validates the decision state: items require at least one entry and a replacement range, and no items means no replacement range.
	/// </summary>
	/// <param name="items">The items carried by the decision.</param>
	/// <param name="replacementRange">The replacement range carried by the decision.</param>
	/// <param name="itemsParameterName">The items parameter name reported by the exception.</param>
	/// <param name="replacementRangeParameterName">The replacement-range parameter name reported by the exception.</param>
	/// <exception cref="ArgumentException">The item/range combination is inconsistent.</exception>
	private static void ValidateState(
		IReadOnlyList<CompletionItem>? items,
		TextRange? replacementRange,
		string itemsParameterName,
		string replacementRangeParameterName
	)
	{
		if (items is null)
		{
			if (replacementRange is not null)
				throw new ArgumentException("A decision without items does not carry a replacement range.", replacementRangeParameterName);

			return;
		}

		if (items.Count == 0)
			throw new ArgumentException("A decision that carries items must supply at least one item.", itemsParameterName);

		if (replacementRange is null)
			throw new ArgumentException("A decision that carries items must supply a replacement range.", replacementRangeParameterName);
	}

	/// <summary>A decision that leaves the current completion session unchanged.</summary>
	/// <remarks>
	/// The value is value-equal to <c>default(CompletionSessionDirective)</c>; use the property to express "leave the session unchanged" explicitly.
	/// </remarks>
	public static CompletionSessionDirective None { get; } = new(false, null, null);

	/// <summary>A decision that dismisses the active completion session.</summary>
	/// <remarks>
	/// Hosts produce this decision when a completion trigger stops applying; the shared <see cref="CompletionSessionKernel"/> never returns it.
	/// </remarks>
	public static CompletionSessionDirective Close { get; } = new(true, null, null);

	/// <summary>A decision that reports no items because the current word filtered every candidate out.</summary>
	/// <remarks>
	/// The shared <see cref="CompletionSessionKernel"/> returns it when the provider returned candidates that the word filter removed completely,
	/// and it does not request a close: the host decides how the session reacts, and a host that does not distinguish the state can treat it exactly
	/// like <see cref="None"/>.
	/// </remarks>
	public static CompletionSessionDirective NoMatches { get; } =
		new(shouldClose: false, items: null, replacementRange: null, allItemsFilteredOut: true);

	/// <summary>Creates a decision that opens or refreshes the completion session for the supplied range.</summary>
	/// <param name="items">The completion items to display; at least one item is required.</param>
	/// <param name="replacementRange">The zero-based replacement range.</param>
	/// <returns>An opening completion-session decision.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="items"/> is empty.</exception>
	/// <remarks>
	/// The decision captures the current contents of <paramref name="items"/>, so later changes to the caller's collection do not affect
	/// the returned decision. An empty list is rejected because an empty session has no presentation; use <see cref="None"/> when the provider
	/// returned nothing and <see cref="NoMatches"/> when the current word filtered every candidate out.
	/// </remarks>
	public static CompletionSessionDirective Open(IReadOnlyList<CompletionItem> items, TextRange replacementRange)
	{
		ArgumentNullException.ThrowIfNull(items);

		if (items.Count == 0)
			throw new ArgumentException("The decision must carry at least one item.", nameof(items));

		return new(false, Array.AsReadOnly([.. items]), replacementRange);
	}

	/// <summary>
	/// Deconstructs the decision into its state components, including <see cref="AllItemsFilteredOut"/> so the <see cref="NoMatches"/> state stays
	/// distinguishable from <see cref="None"/>.
	/// </summary>
	/// <param name="shouldClose">Receives whether the session should be dismissed.</param>
	/// <param name="items">Receives the items to show, when opening.</param>
	/// <param name="replacementRange">Receives the replacement range, when opening.</param>
	/// <param name="allItemsFilteredOut">Receives whether the decision reports a filtered-out result.</param>
	public void Deconstruct(
		out bool shouldClose,
		out IReadOnlyList<CompletionItem>? items,
		out TextRange? replacementRange,
		out bool allItemsFilteredOut
	)
	{
		shouldClose = ShouldClose;
		items = Items;
		replacementRange = ReplacementRange;
		allItemsFilteredOut = AllItemsFilteredOut;
	}
}
