using Nickelony.Matching;

namespace Nickelony.QuickPick;

/// <summary>One entry of a quick pick: what a provider offers and what the list shows for it.</summary>
/// <remarks>
/// <para>
/// A provider returns items without highlights unless it filters by itself; the controller scores the items and sets the highlight
/// ranges of the label and the description. The <see cref="Key"/> identifies the item across queries, so it keeps the active item through a refresh
/// and records the item as recently used.
/// </para>
/// <para>
/// An item that is not <see cref="IsEnabled"/> is listed dimmed and can neither become active nor be accepted.
/// </para>
/// </remarks>
public sealed record QuickPickItem
{
	private IReadOnlyList<MatchRange> _labelHighlights = [];
	private IReadOnlyList<MatchRange> _descriptionHighlights = [];

	/// <param name="key">The identity of the item within its provider, which stays the same from query to query.</param>
	/// <param name="label">The primary text of the item.</param>
	/// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="label"/> is <see langword="null"/>.</exception>
	public QuickPickItem(string key, string label)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(label);

		Key = key;
		Label = label;
	}

	/// <summary>The identity of the item within its provider, which stays the same from query to query.</summary>
	public string Key { get; }

	/// <summary>The primary text of the item, which the query is matched against.</summary>
	public string Label { get; }

	/// <summary>The secondary text shown dimmed after the label, such as a category or a folder, or <see langword="null"/> for none.</summary>
	/// <remarks>
	/// The query is matched against it when the label does not match.
	/// </remarks>
	public string? Description { get; init; }

	/// <summary>A line of further text shown dimmed below the label, or <see langword="null"/> for none.</summary>
	public string? Detail { get; init; }

	/// <summary>The key a host maps to an icon, or <see langword="null"/> for an item without one.</summary>
	public string? IconKey { get; init; }

	/// <summary>
	/// The text shown at the right edge of the row, such as the shortcut of a command or the mark of an unsaved file,
	/// or <see langword="null"/> for none.
	/// </summary>
	public string? TrailingText { get; init; }

	/// <summary>The group the item is listed under, or <see langword="null"/> for an item that belongs to none.</summary>
	public QuickPickGroup? Group { get; init; }

	/// <summary>Whether the item can become active and be accepted; defaults to <see langword="true"/>.</summary>
	public bool IsEnabled { get; init; } = true;

	/// <summary>The characters of the label that the query matched, which the list draws bold; empty for none.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// The controller replaces the ranges of an item that its provider leaves to the filter.
	/// </remarks>
	public IReadOnlyList<MatchRange> LabelHighlights
	{
		get => _labelHighlights;
		init => _labelHighlights = value ?? throw new ArgumentNullException(nameof(LabelHighlights));
	}

	/// <summary>The characters of the description that the query matched, which the list draws bold; empty for none.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public IReadOnlyList<MatchRange> DescriptionHighlights
	{
		get => _descriptionHighlights;
		init => _descriptionHighlights = value ?? throw new ArgumentNullException(nameof(DescriptionHighlights));
	}

	/// <summary>An object the provider attaches for its own use when the item is accepted or previewed; the controller never reads it.</summary>
	/// <remarks>
	/// A command palette puts the command here, so a host whose command ids are not strings needs no mapping from a key.
	/// </remarks>
	public object? Tag { get; init; }
}
