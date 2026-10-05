namespace Nickelony.QuickPick;

/// <summary>One row of the list a quick pick shows: an item, the separator above a group, or the message of an empty list.</summary>
public sealed record QuickPickRow
{
	private QuickPickRow(QuickPickItem? item, string? separatorLabel, string? message)
	{
		Item = item;
		SeparatorLabel = separatorLabel;
		Message = message;
	}

	/// <summary>The item the row shows, or <see langword="null"/> for a separator or a message, which never become active.</summary>
	public QuickPickItem? Item { get; }

	/// <summary>The text of the separator, or <see langword="null"/> for the row of an item or a message.</summary>
	public string? SeparatorLabel { get; }

	/// <summary>
	/// The text of the message row that stands in for an empty list, or <see langword="null"/> for the row of an item or a separator.
	/// </summary>
	public string? Message { get; }

	/// <summary>Whether the row can become active: it shows an enabled item.</summary>
	public bool IsSelectable => Item is { IsEnabled: true };

	/// <summary>Creates the row of an item.</summary>
	/// <param name="item">The item to show.</param>
	/// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
	public static QuickPickRow ForItem(QuickPickItem item)
	{
		ArgumentNullException.ThrowIfNull(item);
		return new QuickPickRow(item, separatorLabel: null, message: null);
	}

	/// <summary>Creates a separator row.</summary>
	/// <param name="label">The text of the separator.</param>
	/// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
	public static QuickPickRow ForSeparator(string label)
	{
		ArgumentNullException.ThrowIfNull(label);
		return new QuickPickRow(item: null, label, message: null);
	}

	/// <summary>Creates the row that tells the user the query left no item.</summary>
	/// <param name="text">The text of the message.</param>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static QuickPickRow ForMessage(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return new QuickPickRow(item: null, separatorLabel: null, text);
	}
}
