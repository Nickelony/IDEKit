namespace Nickelony.QuickPick;

/// <summary>Names a run of items that a quick pick lists together under an optional separator.</summary>
/// <remarks>
/// Items whose groups have the same <see cref="Id"/> form one run, and the items of a run keep the order of their ranking.
/// A run is listed by <see cref="Order"/>, and runs of equal order are listed in the order their first items were ranked, so a provider keeps
/// "recently opened" first under any query by giving that group the lowest order. A group without a <see cref="Label"/> keeps its items together
/// and draws no separator.
/// </remarks>
public sealed record QuickPickGroup
{
	/// <param name="id">The identity of the group, which items of one group share.</param>
	/// <param name="label">The text of the separator above the group, or <see langword="null"/> for a group without a separator.</param>
	/// <param name="showsCount">Whether the separator ends with the number of items the group shows, such as <c>Functions (3)</c>.</param>
	/// <param name="order">The position of the group among the groups, lowest first; items that belong to no group count as order <c>0</c>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
	public QuickPickGroup(string id, string? label = null, bool showsCount = false, int order = 0)
	{
		ArgumentNullException.ThrowIfNull(id);

		Id = id;
		Label = label;
		ShowsCount = showsCount;
		Order = order;
	}

	/// <summary>The identity of the group, which items of one group share.</summary>
	public string Id { get; }

	/// <summary>The text of the separator above the group, or <see langword="null"/> for a group without a separator.</summary>
	public string? Label { get; }

	/// <summary>Whether the separator ends with the number of items the group shows.</summary>
	/// <remarks>
	/// The count follows the filter, so it is the number of items on screen and not the number the provider returned.
	/// </remarks>
	public bool ShowsCount { get; }

	/// <summary>The position of the group among the groups, lowest first.</summary>
	public int Order { get; }
}
