namespace Nickelony.QuickPick;

/// <summary>The input of one accept: the item the user chose and the text they typed.</summary>
public sealed record QuickPickAcceptRequest
{
	/// <param name="item">The accepted item.</param>
	/// <param name="query">The text typed after the prefix of the provider.</param>
	/// <exception cref="ArgumentNullException"><paramref name="item"/> or <paramref name="query"/> is <see langword="null"/>.</exception>
	public QuickPickAcceptRequest(QuickPickItem item, string query)
	{
		ArgumentNullException.ThrowIfNull(item);
		ArgumentNullException.ThrowIfNull(query);

		Item = item;
		Query = query;
	}

	/// <summary>The item the user accepted, which is enabled.</summary>
	public QuickPickItem Item { get; }

	/// <summary>The whole text typed after the prefix of the provider, as it stood when the user accepted.</summary>
	/// <remarks>
	/// A provider that returned only a part of it from <see cref="IQuickPickProvider.GetFilterText"/> reads the rest here, such as the <c>:12</c>
	/// after a file name.
	/// </remarks>
	public string Query { get; }
}
