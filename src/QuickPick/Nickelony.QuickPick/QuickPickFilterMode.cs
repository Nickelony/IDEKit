namespace Nickelony.QuickPick;

/// <summary>Specifies how a quick-pick controller filters and orders the items a provider returns.</summary>
/// <remarks>
/// The enum can grow as quick-pick modes need other rules, so a host that switches over it keeps a default branch. Under every mode
/// but <see cref="None"/> an empty query keeps the order of the provider, with the recent items first when the provider has a recent group.
/// </remarks>
public enum QuickPickFilterMode
{
	/// <summary>The controller keeps the items whose label or description the query matches, and ranks them by score.</summary>
	Fuzzy = 0,

	/// <summary>
	/// The controller matches like <see cref="Fuzzy"/>, reading the label as a file name and the description as the folder that holds it.
	/// </summary>
	Path = 1,

	/// <summary>The controller keeps every item in the order the provider returned it, because the provider has filtered by itself.</summary>
	None = 2,

	/// <summary>
	/// The controller ranks the items by score like <see cref="Fuzzy"/> but drops none, and lists the items the query does not match last
	/// in the order of the provider, because the provider or its server has filtered already.
	/// </summary>
	RankOnly = 3,

	/// <summary>
	/// The controller splits the query into words and keeps the items whose label or description each word matches, in any order of the words,
	/// as a prefix, as the starts of words or as a substring. The items stay in the order of the provider, as in the command palette of VS Code,
	/// and the recent items come first in the order of use, under any query.
	/// </summary>
	Words = 4,
}
