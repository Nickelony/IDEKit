namespace Nickelony.QuickPick;

/// <summary>The input of one query to a quick-pick provider.</summary>
public sealed record QuickPickRequest
{
	/// <param name="query">The text typed after the provider's prefix.</param>
	/// <param name="recentKeys">The keys of the items the user accepted recently, the latest first.</param>
	/// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="recentKeys"/> is <see langword="null"/>.</exception>
	public QuickPickRequest(string query, IReadOnlyList<string> recentKeys)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(recentKeys);

		Query = query;
		RecentKeys = recentKeys;
	}

	/// <summary>The text typed after the provider's prefix, which is empty before the user types.</summary>
	public string Query { get; }

	/// <summary>The keys of the items the user accepted recently in this provider, the latest first.</summary>
	/// <remarks>
	/// A provider that lists only the recent items for an empty query reads the keys here. The list is empty for a provider that keeps
	/// no recent items.
	/// </remarks>
	public IReadOnlyList<string> RecentKeys { get; }
}
