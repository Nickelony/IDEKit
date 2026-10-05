namespace Nickelony.QuickPick;

/// <summary>Supplies the items of one mode of a quick pick, and acts on the item the user chooses.</summary>
/// <remarks>
/// <para>
/// A mode is picked by the text the user types: the provider whose prefix is the longest prefix of the text answers the query
/// that follows the prefix, and the provider with an empty prefix answers a text that starts with no other prefix. A host offers the provider
/// as a <see cref="QuickPickMode"/>, and the prefix of the mode is <see cref="Prefix"/> unless the host gives another. The controller reads
/// the prefix once, when it is created, and calls every other member on its dispatcher thread, so the members that describe the mode can throw
/// without escaping the controller.
/// </para>
/// <para>
/// <see cref="GetItemsAsync"/> is reentrant: the controller starts it on the dispatcher thread for a newer query before the task
/// of an older call completes, and cancels the token of the older call. Its continuation can run on any thread. A provider that ignores
/// the token still returns, and the controller drops the answer. <see cref="AcceptAsync"/> is not reentrant: the controller starts a call
/// only after the task of the earlier call completed, so a slow action never runs twice at once.
/// </para>
/// <para>
/// A provider that previews the active item implements <see cref="Preview"/> and <see cref="CancelPreview"/> as a pair. The first call
/// of <see cref="Preview"/> saves what the preview changes, and <see cref="CancelPreview"/> restores it. When the user accepts an item,
/// the controller calls <see cref="AcceptAsync"/> and never <see cref="CancelPreview"/>, so the accept keeps the preview and the provider forgets
/// what it saved.
/// </para>
/// </remarks>
public interface IQuickPickProvider
{
	/// <summary>
	/// The text that selects this mode by default when the user types it first, or an empty string for the mode that answers any other text.
	/// </summary>
	/// <remarks>
	/// The prefix may end in a space, such as <c>edt </c>. It is compared ordinally, and <c>?</c> is reserved for the list of modes
	/// while the help mode is on. A host that offers the provider under another prefix gives it to <see cref="QuickPickMode"/>. The value stays
	/// the same for the life of the provider, because the recent items are kept by the prefix the mode was offered under.
	/// </remarks>
	string Prefix { get; }

	/// <summary>The name of the mode, which the list of modes shows.</summary>
	string Name { get; }

	/// <summary>The hint shown in the empty text box while the mode is active, and the accessible name of the text box.</summary>
	string Placeholder { get; }

	/// <summary>How the controller filters and orders the items; defaults to <see cref="QuickPickFilterMode.Fuzzy"/>.</summary>
	QuickPickFilterMode FilterMode => QuickPickFilterMode.Fuzzy;

	/// <summary>
	/// The group that lists the recently accepted items first, or <see langword="null"/> when the provider keeps no recent items;
	/// defaults to <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// For a provider that keeps recent items, the controller records the key of each accepted item
	/// and lists the recent items first for an empty query, in the order of use, under this group. For a query, recency breaks a tie in the score,
	/// and <see cref="QuickPickFilterMode.Words"/> keeps the recent items first.
	/// </remarks>
	QuickPickGroup? RecentGroup => null;

	/// <summary>
	/// How long typed text rests before it starts a query in this mode, or <see langword="null"/> to use the delay of the controller;
	/// defaults to <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// A mode whose query costs a request to a server asks for a longer rest than a mode that filters a list in memory. The controller reads
	/// the member on its dispatcher thread each time typed text arms a query, and falls back to its own delay for a value that is negative.
	/// The first query of a session still starts at once.
	/// </remarks>
	TimeSpan? QueryDebounceDelay => null;

	/// <summary>Gets the part of the typed query that the controller filters and ranks the items by.</summary>
	/// <param name="query">The text typed after the prefix.</param>
	/// <returns>The part to filter by; the default is the whole query.</returns>
	/// <remarks>
	/// A mode whose query carries more than a filter returns the filter part, such as the file name of <c>file.cs:12</c>, and reads the rest
	/// from <see cref="QuickPickAcceptRequest.Query"/> when the user accepts. The controller calls this member on its dispatcher thread,
	/// once per query.
	/// </remarks>
	string GetFilterText(string query) => query;

	/// <summary>The text of the one row that the list shows when a query leaves no item; defaults to "No matching results".</summary>
	/// <remarks>
	/// The row is not selectable, so accepting does nothing there. An empty query shows no such row.
	/// </remarks>
	string EmptyText => "No matching results";

	/// <summary>Gets the items that answer a query.</summary>
	/// <param name="request">The query and the recent keys.</param>
	/// <param name="cancellationToken">A token that can cancel the query.</param>
	/// <returns>
	/// The items, in the order the provider prefers. Unless the mode is <see cref="QuickPickFilterMode.None"/>, the provider returns every candidate
	/// and the controller filters them by the query.
	/// </returns>
	/// <remarks>
	/// A provider that cancels its own work, such as a timed-out request, throws an <see cref="OperationCanceledException"/> for a token
	/// of its own. The controller reports that as a failure of the query, because the user did not cancel it.
	/// </remarks>
	Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default);

	/// <summary>Acts on the item the user accepted.</summary>
	/// <param name="request">The accepted item, which is enabled, and the text the user typed.</param>
	/// <param name="cancellationToken">A token that the controller cancels when it is disposed.</param>
	/// <returns>A task that completes when the action ended.</returns>
	/// <remarks>
	/// The controller has closed the session before it calls this member, so the action may open another session.
	/// </remarks>
	Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default);

	/// <summary>Shows the effect of the active item without accepting it, such as a caret moved to a line; the default does nothing.</summary>
	/// <param name="item">The active item, which is enabled.</param>
	void Preview(QuickPickItem item) { }

	/// <summary>Undoes what <see cref="Preview"/> changed after a session ended without an accept; the default does nothing.</summary>
	/// <remarks>
	/// The controller calls this member once after a run of previews, and only when no accept followed them.
	/// </remarks>
	void CancelPreview() { }

	/// <summary>
	/// Invoked after the mode stopped being the mode of the open session, so the provider releases what it kept for the stay;
	/// the default does nothing.
	/// </summary>
	/// <remarks>
	/// The controller calls this member on its dispatcher thread once per stay in the mode: after the session ended, with or without an accept,
	/// and after typed text switched to another mode. It runs after <see cref="CancelPreview"/> and before <see cref="AcceptAsync"/>, so an accept
	/// uses the item and not what the provider kept. The mode starts a new stay with its next query.
	/// </remarks>
	void OnDeactivated() { }
}
