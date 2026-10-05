namespace Nickelony.QuickPick;

/// <summary>
/// Drives one quick pick: it picks the provider by the prefix of the typed text, queries it, ranks the items,
/// keeps the active item, previews it, and accepts it.
/// </summary>
/// <remarks>
/// <para>
/// The controller presents nothing itself. A presenter, or a host window, reads <see cref="State"/> when <see cref="Changed"/> is raised
/// and calls <see cref="SetText"/>, <see cref="MoveActive"/>, <see cref="SetActiveRow"/>, <see cref="AcceptAsync"/> and <see cref="Dismiss"/>
/// for the user's input. The controller belongs to the thread that created it, its dispatcher thread: it starts the queries, calls the providers
/// and publishes its state there, and the continuation of a query returns there through the synchronization context of that thread.
/// Only <see cref="Dispose"/> may run on another thread, and it hands the teardown to the dispatcher thread.
/// </para>
/// <para>
/// Typed text starts a query after <see cref="QuickPickControllerOptions.QueryDebounceDelay"/>,
/// and the first query of a session starts at once. A newer text cancels the query in flight and drops its answer even when the provider
/// ignores the token, so an answer to an older text never shows; the list keeps the rows of the last answer until the new one arrives.
/// A query that runs past <see cref="QuickPickControllerOptions.BusyDelay"/> reports <see cref="QuickPickPresentationState.IsBusy"/>.
/// A provider that cancels its own query is a failed query. While the help mode is on, a text that starts with the reserved prefix <c>?</c>,
/// or that no mode claims when no mode has an empty prefix, lists the modes instead. A query that leaves no item shows one row that holds
/// <see cref="IQuickPickProvider.EmptyText"/> and cannot become active. A list of many items is ranked on a pool thread, so a keystroke does not hold
/// the dispatcher.
/// </para>
/// <para>
/// The first selectable row is active after each answer, except that a <see cref="Refresh"/> keeps the item that is active when the answer arrives.
/// The active item is previewed through its provider; a session that ends without an accept, a change of mode, and an answer without an active
/// item cancel the preview. <see cref="AcceptAsync"/> waits for the answer to the text that was typed, closes the session, records the item
/// as recently used, and then calls the provider, once per session whatever the number of calls. The accepts of the providers run one after another.
/// </para>
/// <para>
/// Failures of a provider, of the recent store and of a host callback are contained and passed to the fault callback, so none escapes the controller.
/// </para>
/// </remarks>
public sealed class QuickPickController : IDisposable
{
	// A list longer than this is ranked on a pool thread; a shorter one is cheaper to rank than to hand over.
	private const int OffThreadRankingThreshold = 500;

	private readonly QuickPickModeResolver _resolver;
	private readonly QuickPickDispatcher _dispatcher;
	private readonly QuickPickControllerOptions _options;
	private readonly Action<Exception>? _reportFault;
	private readonly QuickPickRecentTracker _recents;
	private readonly QuickPickPreviewTracker _preview;
	private readonly QuickPickAcceptGate _acceptGate = new();
	private readonly RequestLifetime _lifetime = new();
	private readonly DispatcherDebouncer _queryDebouncer;
	private readonly QuickPickBusyTimer _busyTimer;

	private IQuickPickProvider _mode;

	private string _text = string.Empty;
	private string _query = string.Empty;

	private IReadOnlyList<QuickPickRow> _rows = [];
	private int _activeRowIndex = -1;

	private bool _keepsActiveItem;
	private bool _isOpen;
	private bool _isBusy;

	private Task _queryTask = Task.CompletedTask;

	// A session has a number, so a call that waited across the end of its session can tell. The number of the session whose accept is
	// waiting or running is kept to refuse a second accept of that session.
	private int _session;
	private int _acceptingSession = -1;

	/// <param name="modes">
	/// The modes the host offers, each a provider under a prefix. No two share a prefix, and a quick pick whose help mode is off needs
	/// one with the empty prefix.
	/// </param>
	/// <param name="recentStore">
	/// The store that keeps the recently accepted items between runs, or <see langword="null"/> to keep them for the life of the controller.
	/// </param>
	/// <param name="reportFault">
	/// Receives each failure the controller contained: a provider that threw or canceled its own query, a store, a handler of <see cref="Changed"/>.
	/// It runs on the thread that created the controller, also for a failure that <see cref="Dispose"/> contains. It must not throw.
	/// A <see langword="null"/> value drops the failures.
	/// </param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="timeProvider">
	/// The source of the timers behind the debounce and the busy delay, or <see langword="null"/> for the system clock.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="modes"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="modes"/> holds a <see langword="null"/> entry, a mode without a prefix, a mode with the reserved prefix <c>?</c>
	/// while <see cref="QuickPickControllerOptions.EnableHelpMode"/> is on, two modes with the same prefix, or one provider in two modes; or the help
	/// mode is off and no mode has the empty prefix.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The creating thread has no <see cref="SynchronizationContext.Current"/>, or its context is the base <see cref="SynchronizationContext"/> type.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The thread that creates the controller is its dispatcher thread, and every member but <see cref="Dispose"/> throws
	/// when another thread calls it; a <see cref="Dispose"/> from another thread posts its teardown to the dispatcher thread. That thread needs
	/// a single-threaded <see cref="SynchronizationContext.Current"/>, which the controller takes as its only route back to the thread: its timers,
	/// the answers of its providers and every <see langword="await"/> of its own resume through it. The UI thread of a WPF, Avalonia or Windows Forms
	/// host has one; a console host installs a pump of its own first. The base <see cref="SynchronizationContext"/> type posts to the thread pool,
	/// so the controller refuses it.
	/// </para>
	/// </remarks>
	public QuickPickController(
		IReadOnlyList<QuickPickMode> modes,
		IQuickPickRecentStore? recentStore = null,
		Action<Exception>? reportFault = null,
		QuickPickControllerOptions? options = null,
		TimeProvider? timeProvider = null
	)
	{
		ArgumentNullException.ThrowIfNull(modes);

		_options = options ?? QuickPickControllerOptions.Default;
		_resolver = new QuickPickModeResolver(modes, _options);
		_dispatcher = new QuickPickDispatcher(timeProvider ?? TimeProvider.System);
		_reportFault = reportFault;
		_recents = new QuickPickRecentTracker(recentStore, _options.MaxRecentCount, ReportFault);
		_preview = new QuickPickPreviewTracker(ReportFault);
		_queryDebouncer = new DispatcherDebouncer(_options.QueryDebounceDelay, _dispatcher);
		_busyTimer = new QuickPickBusyTimer(_dispatcher, _options.BusyDelay);
		_mode = _resolver.Default;
	}

	/// <summary>The state the controller last published.</summary>
	public QuickPickPresentationState State { get; private set; } = QuickPickPresentationState.Closed;

	/// <summary>Raised after <see cref="State"/> changed, on the dispatcher thread.</summary>
	public event EventHandler? Changed;

	/// <summary>Opens a session with the given text, replacing an open session.</summary>
	/// <param name="text">The text of the input, including the prefix that picks the mode; empty for the default mode.</param>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The session starts its first query at once. An open session ends without an accept, so its preview is canceled.
	/// </remarks>
	public void Open(string text = "")
	{
		ArgumentNullException.ThrowIfNull(text);

		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		EndSession(cancelPreview: true);

		_isOpen = true;
		SelectMode(text);
		Publish();
		RunQuery();
	}

	/// <summary>Changes the text of the input of an open session, as typing does.</summary>
	/// <param name="text">The new text of the input, including the prefix that picks the mode.</param>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The call does nothing when no session is open or the text is the current one.
	/// </remarks>
	public void SetText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		if (_isOpen && text != _text)
			ChangeText(text, startNow: false);
	}

	/// <summary>Runs the current query again and keeps the active item when the new answer holds its key.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// A host calls it when the candidates of the provider changed, such as a file list after a file was created. The item that stays active
	/// is the one that is active when the answer arrives, so a move of the user while the query runs holds.
	/// </remarks>
	public void Refresh()
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		if (!_isOpen)
			return;

		_keepsActiveItem = true;
		RunQuery();
	}

	/// <summary>Moves the active row by a number of selectable rows, skipping separators and disabled items.</summary>
	/// <param name="delta">The number of selectable rows to move; negative moves up.</param>
	/// <param name="wrap">
	/// <see langword="true"/> to continue from the opposite end when the move passes an end; <see langword="false"/> to stop at the end, as a page
	/// move does.
	/// </param>
	/// <returns><see langword="true"/> when a different row became active; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	public bool MoveActive(int delta, bool wrap = true)
	{
		if (_lifetime.IsDisposed)
			return false;

		_dispatcher.VerifyAccess();

		if (!_isOpen)
			return false;

		List<int> selectable = [];

		for (int index = 0; index < _rows.Count; index++)
		{
			if (_rows[index].IsSelectable)
				selectable.Add(index);
		}

		if (selectable.Count == 0 || delta == 0)
			return false;

		int position = selectable.IndexOf(_activeRowIndex);

		// Without an active row, a move down starts at the first row and a move up at the last.
		int target = position < 0 ? (delta > 0 ? delta - 1 : selectable.Count + delta) : position + delta;
		target = wrap ? ((target % selectable.Count) + selectable.Count) % selectable.Count : Math.Clamp(target, 0, selectable.Count - 1);

		return ActivateRow(selectable[target]);
	}

	/// <summary>Makes a row active, as a click or a pointer move does.</summary>
	/// <param name="rowIndex">The index in <see cref="QuickPickPresentationState.Rows"/> of an enabled item.</param>
	/// <returns>
	/// <see langword="true"/> when the row became active; <see langword="false"/> when the row is already active, is a separator,
	/// holds a disabled item, or does not exist.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	public bool SetActiveRow(int rowIndex)
	{
		if (_lifetime.IsDisposed)
			return false;

		_dispatcher.VerifyAccess();

		return _isOpen && rowIndex >= 0 && rowIndex < _rows.Count && _rows[rowIndex].IsSelectable && ActivateRow(rowIndex);
	}

	/// <summary>Accepts the active item: it waits for the answer to the typed text, closes the session, and calls the provider.</summary>
	/// <returns>
	/// A task that completes after the provider's accept ended, and reports <see langword="true"/>
	/// when an item was accepted. It reports <see langword="false"/> when no session is open, another call is already accepting, no item is active,
	/// or the session ended while the call waited. Accepting a mode in the list of modes switches to that mode, keeps the session open,
	/// and reports <see langword="true"/>. The task does not fault: a failure of the provider is reported.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the controller's dispatcher thread; the exception is thrown by the call.
	/// </exception>
	/// <remarks>
	/// Whatever the number of calls, a session accepts at most once, because the session is closed before the provider runs.
	/// A call that arrives while an earlier call of the same session still waits for an answer returns <see langword="false"/> at once, and a call
	/// that waited when its session ended accepts nothing, even in a session that opened since. The provider's accept starts when the earlier accept
	/// of any session completed.
	/// </remarks>
	public Task<bool> AcceptAsync()
	{
		if (_lifetime.IsDisposed)
			return Task.FromResult(false);

		_dispatcher.VerifyAccess();

		return AcceptCoreAsync();
	}

	/// <summary>Ends the session without accepting, and cancels the preview.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The call does nothing when no session is open.
	/// </remarks>
	public void Dismiss()
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		if (!_isOpen)
			return;

		EndSession(cancelPreview: true);
		Publish();
	}

	/// <summary>Ends the session, cancels the preview and in-flight query, and stops the controller.</summary>
	/// <remarks>
	/// Disposal is idempotent and does not verify the thread, because it runs during the teardown of the host. On the dispatcher
	/// thread it completes before it returns. From another thread, it makes every member return without effect and cancels the query
	/// in flight at once, then posts the rest to the dispatcher thread: ending the session, the preview and deactivation hooks, and the closed
	/// <see cref="State"/> that raises <see cref="Changed"/> run there when the thread pumps its context. A dispatcher thread that is gone drops
	/// the posted part without a fault report.
	/// </remarks>
	public void Dispose()
	{
		if (_dispatcher.CheckAccess())
		{
			TearDown();
			return;
		}

		// Another thread touches only what is thread-safe: the lifetime, which makes every member inert and cancels the work in flight. The state of
		// the controller belongs to the dispatcher thread, so the teardown that touches it and calls the host goes there. A disposal that finds the
		// lifetime disposed has nothing to add, and two that race both post, which the teardown absorbs.
		if (_lifetime.IsDisposed)
			return;

		_lifetime.Dispose();
		_ = _dispatcher.TryPost(TearDown);
	}

	// Runs on the dispatcher thread only. A repeated run finds the session ended and the preview forgotten, so it does nothing: a disposal on the
	// dispatcher thread can finish a teardown that another thread posted before the thread pumped, and the posted run then has nothing left.
	private void TearDown()
	{
		bool wasOpen = _isOpen;

		EndSession(cancelPreview: true);

		_lifetime.Dispose();
		_queryDebouncer.Dispose();

		if (wasOpen)
			Publish();
	}

	private void ReportFault(Exception exception) => _reportFault?.Invoke(exception);

	// A member of a provider that describes the mode can throw, and the controller then uses the fallback instead of failing the session.
	private T Read<T>(Func<T> read, T fallback) => CallbackContainment.Invoke(read, fallback, ReportFault);

	private void SelectMode(string text)
	{
		_text = text;
		(_mode, _query) = _resolver.Resolve(text);
	}

	private void ChangeText(string text, bool startNow)
	{
		IQuickPickProvider previousMode = _mode;

		SelectMode(text);
		_keepsActiveItem = false;

		// The answer to the older text must never show, so the query in flight is canceled and its answer is made stale.
		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();

		if (!ReferenceEquals(previousMode, _mode))
		{
			// The rows belong to the mode that answered them, and so does the preview.
			_preview.Cancel();
			Deactivate(previousMode);
			_busyTimer.Stop();
			_rows = [];
			_activeRowIndex = -1;
			_isBusy = false;
		}

		Publish();

		if (startNow)
			RunQuery();
		else
			_queryDebouncer.Arm(RunQuery, ReadQueryDelay(_mode));
	}

	// A mode may ask for its own rest before a query; a missing or negative value falls back to the delay of the controller.
	private TimeSpan ReadQueryDelay(IQuickPickProvider mode) =>
		Read<TimeSpan?>(() => mode.QueryDebounceDelay, fallback: null) is { } delay && delay >= TimeSpan.Zero ? delay : _options.QueryDebounceDelay;

	private void Deactivate(IQuickPickProvider mode) => CallbackContainment.TryRun(mode.OnDeactivated, ReportFault);

	private void RunQuery()
	{
		if (_lifetime.IsDisposed || !_isOpen)
			return;

		_queryDebouncer.Cancel();

		int generation = _lifetime.AdvanceGeneration();
		IQuickPickProvider provider = _mode;
		string query = _query;

		// The ranking runs on any thread, so what it needs from the provider is read here, on the dispatcher thread.
		QuickPickGroup? recentGroup = Read(() => provider.RecentGroup, fallback: null);
		QuickPickFilterMode filterMode = Read(() => provider.FilterMode, QuickPickFilterMode.Fuzzy);
		string? emptyText = query.Trim().Length > 0 ? Read<string?>(() => provider.EmptyText, fallback: null) : null;
		IReadOnlyList<string> recentKeys = recentGroup is null ? [] : _recents.GetRecentKeys(_resolver.PrefixOf(provider));
		string filterText = Read(() => provider.GetFilterText(query), query) ?? query;

		StartBusyTimer(generation);

		_queryTask = RunQueryAsync(
			new QueryPlan(provider, Request: new QuickPickRequest(query, recentKeys), filterText, filterMode, recentGroup, emptyText, generation)
		);
	}

	private async Task RunQueryAsync(QueryPlan plan)
	{
		try
		{
			// The lifetime cancels the previous query when a newer one starts and drops its answer even if the provider ignored the token. The
			// provider starts on this thread and the ranking runs wherever the answer arrives or on a pool thread, while the publication and the
			// failure report resume on the synchronization context of the creating thread, which is the dispatcher thread.
			RequestOutcome outcome = await _lifetime
				.RunAsync(
					state: plan,
					computeAsync: ComputeRowsAsync,
					apply: rows => ApplyRows(plan.Generation, rows),

					onFailure: exception =>
					{
						ReportFault(exception);
						CallbackContainment.TryRun(() => ApplyFailure(plan.Generation), ReportFault);
					}
				)
				.ConfigureAwait(true);

			// A cancellation that neither the controller nor the user asked for, such as a timeout of the provider's own client, still
			// belongs to the current query, so it ends the wait and reports the failure.
			if (outcome == RequestOutcome.Canceled && _isOpen && _lifetime.IsCurrent(plan.Generation))
			{
				ReportFault(new OperationCanceledException("The provider canceled its own query."));
				ApplyFailure(plan.Generation);
			}
		}
		catch (OperationCanceledException)
		{
			// A superseded or dismissed query reports cancellation, not a failure.
		}
		catch (Exception)
		{
			// The failure callback reported the exception when this query still owned the slot; a superseded query's failure carries no information.
		}
	}

	private async Task<IReadOnlyList<QuickPickRow>> ComputeRowsAsync(QueryPlan plan, CancellationToken cancellationToken)
	{
		IReadOnlyList<QuickPickItem> items = await plan.Provider.GetItemsAsync(plan.Request, cancellationToken).ConfigureAwait(false) ?? [];

		IReadOnlyList<QuickPickRow> rows = await RankAsync(
				filterMode: plan.FilterMode,
				recentGroup: plan.RecentGroup,
				items,
				filterText: plan.FilterText,
				recentKeys: plan.Request.RecentKeys,
				cancellationToken
			)
			.ConfigureAwait(false);

		// A query that leaves no item shows one message row instead of an empty box.
		return rows.Count == 0 && plan.EmptyText is not null ? [QuickPickRow.ForMessage(plan.EmptyText)] : rows;
	}

	private Task<IReadOnlyList<QuickPickRow>> RankAsync(
		QuickPickFilterMode filterMode,
		QuickPickGroup? recentGroup,
		IReadOnlyList<QuickPickItem> items,
		string filterText,
		IReadOnlyList<string> recentKeys,
		CancellationToken cancellationToken
	)
	{
		if (items.Count > OffThreadRankingThreshold && _dispatcher.CheckAccess())
		{
			return Task.Run(
				() =>
					QuickPickRanker.Rank(
						filterMode,
						recentGroup,
						items,
						query: filterText,
						recentKeys,
						maxItemCount: _options.MaxItemCount,
						cancellationToken
					),

				cancellationToken
			);
		}

		return Task.FromResult(
			QuickPickRanker.Rank(
				filterMode,
				recentGroup,
				items,
				query: filterText,
				recentKeys,
				maxItemCount: _options.MaxItemCount,
				cancellationToken
			)
		);
	}

	private void ApplyRows(int generation, IReadOnlyList<QuickPickRow> rows)
	{
		if (!_isOpen || !_lifetime.IsCurrent(generation))
			return;

		_busyTimer.Stop();

		// A refresh keeps the item that is active now, which is not the item that was active when the query started.
		string? keptKey = _keepsActiveItem ? State.ActiveItem?.Key : null;

		_keepsActiveItem = false;
		_isBusy = false;
		_rows = rows;
		_activeRowIndex = ChooseActiveRow(rows, keptKey);

		Publish();
		SyncPreview();
	}

	private void ApplyFailure(int generation)
	{
		if (!_isOpen || !_lifetime.IsCurrent(generation))
			return;

		_busyTimer.Stop();

		_keepsActiveItem = false;
		_isBusy = false;
		_rows = [];
		_activeRowIndex = -1;

		Publish();
		SyncPreview();
	}

	private static int ChooseActiveRow(IReadOnlyList<QuickPickRow> rows, string? preferredKey)
	{
		if (preferredKey is not null)
		{
			for (int index = 0; index < rows.Count; index++)
			{
				if (rows[index] is { IsSelectable: true, Item: { } item } && item.Key == preferredKey)
					return index;
			}
		}

		for (int index = 0; index < rows.Count; index++)
		{
			if (rows[index].IsSelectable)
				return index;
		}

		return -1;
	}

	private bool ActivateRow(int rowIndex)
	{
		if (rowIndex == _activeRowIndex)
			return false;

		_activeRowIndex = rowIndex;

		Publish();
		SyncPreview();

		return true;
	}

	private bool IsSessionOpen(int session) => !_lifetime.IsDisposed && _isOpen && _session == session;

	private async Task<bool> AcceptCoreAsync()
	{
		if (!_isOpen || _acceptingSession == _session)
			return false;

		int session = _session;

		_acceptingSession = session;

		IQuickPickProvider provider;
		QuickPickAcceptRequest request;

		try
		{
			// An accept that follows typing waits for the answer to the text that was typed, so it never accepts a row of an older text.
			await CompleteQueryAsync(session).ConfigureAwait(true);

			if (!IsSessionOpen(session) || State.ActiveItem is not { IsEnabled: true } active)
				return false;

			provider = _mode;
			request = new QuickPickAcceptRequest(active, _query);

			if (ReferenceEquals(provider, _resolver.Help))
			{
				// Accepting a mode of the list switches to that mode and keeps the session open.
				ChangeText(request.Item.Key, startNow: true);
				return true;
			}

			RecordRecent(provider, request.Item);

			// The accept keeps what the preview changed, so the provider is not asked to cancel it.
			EndSession(cancelPreview: false);
			Publish();
		}
		finally
		{
			// Only the accept of the session that is still current holds the flag; a session that opened since has set its own.
			if (_acceptingSession == session)
				_acceptingSession = -1;
		}

		await _acceptGate.RunAsync(() => AcceptInProviderAsync(provider, request)).ConfigureAwait(true);

		return true;
	}

	// Starts the query that typing scheduled and waits until the newest query of the session has answered or has been dropped.
	private async Task CompleteQueryAsync(int session)
	{
		while (IsSessionOpen(session))
		{
			if (_queryDebouncer.IsPending)
				RunQuery();

			Task query = _queryTask;

			if (query.IsCompleted)
				return;

			await query.ConfigureAwait(true);
		}
	}

	private async Task AcceptInProviderAsync(IQuickPickProvider provider, QuickPickAcceptRequest request)
	{
		try
		{
			await provider.AcceptAsync(request, _lifetime.LifetimeToken).ConfigureAwait(true);
		}
		catch (OperationCanceledException) when (_lifetime.IsDisposed)
		{
			// The controller was disposed while the provider accepted, which ends the accept.
		}
		catch (Exception exception)
		{
			ReportFault(exception);
		}
	}

	private void RecordRecent(IQuickPickProvider provider, QuickPickItem item)
	{
		if (Read(() => provider.RecentGroup, fallback: null) is not null)
			_recents.Record(_resolver.PrefixOf(provider), item.Key);
	}

	// Previews the active item through the mode that listed it, and cancels the preview of the previous item when nothing can be previewed.
	private void SyncPreview()
	{
		if (!_isOpen || State.ActiveItem is not { IsEnabled: true } item)
		{
			_preview.Cancel();
			return;
		}

		_preview.Show(_mode, item);
	}

	private void EndSession(bool cancelPreview)
	{
		_queryDebouncer.Cancel();
		_busyTimer.Stop();

		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();

		if (cancelPreview)
			_preview.Cancel();
		else
			_preview.Forget();

		if (_isOpen)
			Deactivate(_mode);

		// The next session is a different one, and an accept that waits for this one finds the number changed.
		_session++;
		_acceptingSession = -1;
		_isOpen = false;
		_isBusy = false;
		_keepsActiveItem = false;
		_text = string.Empty;
		_query = string.Empty;
		_rows = [];
		_activeRowIndex = -1;
		_mode = _resolver.Default;
	}

	private void StartBusyTimer(int generation)
	{
		_busyTimer.Start(() =>
		{
			if (_isOpen && !_isBusy && _lifetime.IsCurrent(generation))
			{
				_isBusy = true;
				Publish();
			}
		});
	}

	private void Publish()
	{
		State = _isOpen
			? new QuickPickPresentationState
			{
				IsOpen = true,
				Text = _text,
				Placeholder = Read(() => _mode.Placeholder, fallback: string.Empty) ?? string.Empty,
				Rows = _rows,
				ActiveRowIndex = _activeRowIndex,
				IsBusy = _isBusy,
			}
			: QuickPickPresentationState.Closed;

		CallbackContainment.TryRun(() => Changed?.Invoke(this, EventArgs.Empty), ReportFault);
	}

	// What one query needs from the provider, read on the dispatcher thread because the ranking can run on any thread.
	private readonly record struct QueryPlan(
		IQuickPickProvider Provider,
		QuickPickRequest Request,
		string FilterText,
		QuickPickFilterMode FilterMode,
		QuickPickGroup? RecentGroup,
		string? EmptyText,
		int Generation
	);
}
