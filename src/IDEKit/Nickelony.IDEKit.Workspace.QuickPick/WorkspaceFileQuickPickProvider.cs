using Nickelony.IDEKit.Core.Callbacks;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.FileWatching;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>Lists the files below the workspace roots in the quick-pick mode that has no prefix, and opens the file the user picks.</summary>
/// <remarks>
/// <para>
/// Each file shows by its name, with its folder below its root as the description, and the mode matches a query
/// as a path: a query without a separator prefers the file name. Two groups are listed, whatever the query: <c>recently opened</c> holds the files
/// the user picked before, in the order of use, and the open documents of the host, and <c>file results</c> holds
/// the other files. A document with unsaved changes shows <see cref="WorkspaceFileQuickPickOptions.UnsavedText"/> at the right
/// edge and carries <see cref="OpenEditorDescriptor.UnsavedIconKey"/>. An open document outside the roots is listed too. A file that was picked
/// before drops out of the group once it is deleted or excluded.
/// </para>
/// <para>
/// A query may end in <c>:line</c> or <c>:line:column</c>, as in <c>file.cs:12</c>. The mode ranks by the text before the colon,
/// and accepting the file passes the line and the column to the open callback, which clamps them to the document it opens.
/// </para>
/// <para>
/// The first query walks the roots on a pool thread, so the first
/// list can take a moment and the query reports a busy state meanwhile. After that, the list follows a file watcher per root and a query never walks:
/// a file or folder that is created, deleted or moved shows up or goes away when the watcher reports it, and <see cref="FilesChanged"/> tells
/// the host to call <c>QuickPickController.Refresh</c>. The list holds at most <see cref="WorkspaceFileQuickPickOptions.MaxFileCount"/> files.
/// A root that does not exist loses its files. A root whose watcher cannot start keeps the files it had, and a later query tries
/// to watch it again, after a pause.
/// </para>
/// <para>
/// The provider is thread-safe apart from what <see cref="IQuickPickProvider"/> asks of the dispatcher thread. A query that is still running
/// when the provider is disposed ends as canceled. Dispose the provider when the host closes the quick pick for good, which stops the watchers.
/// </para>
/// </remarks>
public sealed class WorkspaceFileQuickPickProvider : IQuickPickProvider, IDisposable
{
	private readonly WorkspaceFileTracker _tracker;
	private readonly Func<WorkspaceFileOpenRequest, CancellationToken, Task> _openFileAsync;
	private readonly Func<IReadOnlyList<OpenEditorDescriptor>>? _listOpenEditors;
	private readonly WorkspaceFileQuickPickOptions _options;
	private readonly Action<Exception> _reportFault;

	/// <param name="rootPaths">The workspace roots, as absolute paths. A path that is not a folder lists no files.</param>
	/// <param name="pathComparison">
	/// The comparison that decides which paths are one file and how the casing of a glob reads. Pass the one the document store of the host
	/// uses, so the file list and the documents cannot disagree about which paths are one file.
	/// </param>
	/// <param name="openFileAsync">
	/// Opens a file, with the line and column typed after its name, when the user accepts it. It runs on the dispatcher thread.
	/// </param>
	/// <param name="listOpenEditors">
	/// Lists the open documents of the host, which the mode shows first and marks when they have unsaved changes,
	/// or <see langword="null"/> for a host that lists none. It runs on the dispatcher thread.
	/// </param>
	/// <param name="options">The options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="reportFault">
	/// Receives each failure the provider contained: a watcher that failed or could not start, a folder walk that threw,
	/// a handler of <see cref="FilesChanged"/>, or <paramref name="listOpenEditors"/>. It must not throw, and it runs on the thread
	/// that met the failure: a pool thread for a watcher, a walk or a handler, and the dispatcher thread for <paramref name="listOpenEditors"/>,
	/// so it touches no UI. A <see langword="null"/> value drops the failures.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="rootPaths"/> or <paramref name="openFileAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">A root is <see langword="null"/>, empty, not an absolute path, or listed twice.</exception>
	public WorkspaceFileQuickPickProvider(
		IReadOnlyList<string> rootPaths,
		LocalPathComparisonPolicy pathComparison,
		Func<WorkspaceFileOpenRequest, CancellationToken, Task> openFileAsync,
		Func<IReadOnlyList<OpenEditorDescriptor>>? listOpenEditors = null,
		WorkspaceFileQuickPickOptions? options = null,
		Action<Exception>? reportFault = null
	)
		: this(
			tracker: CreateTracker(
				rootPaths: rootPaths ?? throw new ArgumentNullException(nameof(rootPaths)),
				pathComparison,
				options: options ?? WorkspaceFileQuickPickOptions.Default,
				reportFault: reportFault ?? Ignore
			),

			openFileAsync: openFileAsync ?? throw new ArgumentNullException(nameof(openFileAsync)),
			listOpenEditors,
			options: options ?? WorkspaceFileQuickPickOptions.Default,
			reportFault: reportFault ?? Ignore
		) { }

	/// <param name="tracker">The list of files, which a test builds over a substituted file system and watcher.</param>
	/// <param name="openFileAsync">Opens a file.</param>
	/// <param name="listOpenEditors">Lists the open documents of the host, or <see langword="null"/> for none.</param>
	/// <param name="options">The options.</param>
	/// <param name="reportFault">Receives each failure the provider contained.</param>
	internal WorkspaceFileQuickPickProvider(
		WorkspaceFileTracker tracker,
		Func<WorkspaceFileOpenRequest, CancellationToken, Task> openFileAsync,
		Func<IReadOnlyList<OpenEditorDescriptor>>? listOpenEditors,
		WorkspaceFileQuickPickOptions options,
		Action<Exception> reportFault
	)
	{
		RecentGroup = new QuickPickGroup("recent", options.RecentGroupLabel, order: 0);
		_tracker = tracker;
		_openFileAsync = openFileAsync;
		_listOpenEditors = listOpenEditors;
		_options = options;
		_reportFault = reportFault;

		_tracker.Changed += (_, _) => FilesChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Raised on a pool thread after a file or folder was created, deleted or moved, or after the roots were walked again.</summary>
	/// <remarks>
	/// A host that keeps the quick pick open marshals to its dispatcher thread and calls <c>QuickPickController.Refresh</c>, which keeps the active
	/// item when the new list still holds it. A handler that throws is contained and reported.
	/// </remarks>
	public event EventHandler? FilesChanged;

	/// <inheritdoc/>
	public string Prefix => string.Empty;

	/// <inheritdoc/>
	public string Name => _options.Name;

	/// <inheritdoc/>
	public string Placeholder => _options.Placeholder;

	/// <inheritdoc/>
	public QuickPickFilterMode FilterMode => QuickPickFilterMode.Path;

	/// <inheritdoc/>
	public QuickPickGroup? RecentGroup { get; }

	/// <inheritdoc/>
	public string EmptyText => _options.EmptyText;

	/// <inheritdoc/>
	public string GetFilterText(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		return SplitQuery(query).Name;
	}

	/// <inheritdoc/>
	/// <exception cref="ObjectDisposedException">The provider was disposed before the call.</exception>
	/// <exception cref="OperationCanceledException">The provider was disposed while the query ran.</exception>
	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// The open documents belong to the UI thread, so they are read here, before the work moves to a pool thread.
		IReadOnlyList<OpenEditorDescriptor> editors = _listOpenEditors is null
			? []
			: CallbackContainment.Invoke<IReadOnlyList<OpenEditorDescriptor>>(_listOpenEditors, [], _reportFault) ?? [];

		return GetItemsCoreAsync(request.RecentKeys, editors, cancellationToken);
	}

	/// <inheritdoc/>
	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		(_, LineColumnQuery? position) = SplitQuery(request.Query);

		return _openFileAsync(new WorkspaceFileOpenRequest(request.Item.Key, position?.Line, position?.Column), cancellationToken);
	}

	/// <summary>Stops the file watchers and empties the list.</summary>
	public void Dispose() => _tracker.Dispose();

	private static void Ignore(Exception exception) { }

	private static WorkspaceFileTracker CreateTracker(
		IReadOnlyList<string> rootPaths,
		LocalPathComparisonPolicy pathComparison,
		WorkspaceFileQuickPickOptions options,
		Action<Exception> reportFault
	)
	{
		return new WorkspaceFileTracker(
			rootPaths,
			filter: new WorkspaceFileFilter(options.ExcludeGlobs, pathComparison),
			maxFileCount: options.MaxFileCount,

			createWatcher: (root, dispatchAsync, onWatcherFailed) =>
				new FileWatcher(
					workspaceRootDirectoryPath: root,
					dispatchAsync,
					watchSpecifications: [new FileWatchSpecification("*", IncludeSubdirectories: true)],
					onWatcherFailed: (watcher, exception) => onWatcherFailed(watcher, exception),
					pathComparison
				),

			enumerate: WorkspaceFileWalker.List,
			pathComparison,
			fileGroup: new QuickPickGroup("files", options.FileGroupLabel, order: 1),
			timeProvider: TimeProvider.System,
			reportFault
		);
	}

	// Splits a query into the file name part and the line and column typed after it, as in file.cs:12 or file.cs:12:5.
	private static (string Name, LineColumnQuery? Position) SplitQuery(string query)
	{
		string trimmed = query.TrimEnd();
		int last = trimmed.LastIndexOf(':');

		if (last < 0)
			return (query, null);

		string tail = trimmed[(last + 1)..].Trim();

		// A trailing colon is a position that is still being typed, so the text before it is read as it is.
		if (tail.Length == 0)
			return SplitQuery(trimmed[..last]);

		if (!IsDigits(tail))
			return (query, null);

		int previous = last > 0 ? trimmed.LastIndexOf(':', last - 1) : -1;

		if (
			previous >= 0
			&& IsDigits(trimmed.AsSpan(previous + 1, last - previous - 1).Trim())
			&& LineColumnQuery.TryParse(trimmed[(previous + 1)..], out LineColumnQuery withColumn)
		)
		{
			return (trimmed[..previous], withColumn);
		}

		return LineColumnQuery.TryParse(tail, out LineColumnQuery lineOnly) ? (trimmed[..last], lineOnly) : (query, null);
	}

	private static bool IsDigits(ReadOnlySpan<char> text)
	{
		if (text.IsEmpty)
			return false;

		foreach (char character in text)
		{
			if (character is < '0' or > '9')
				return false;
		}

		return true;
	}

	private async Task<IReadOnlyList<QuickPickItem>> GetItemsCoreAsync(
		IReadOnlyList<string> recentKeys,
		IReadOnlyList<OpenEditorDescriptor> editors,
		CancellationToken cancellationToken
	)
	{
		await _tracker.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

		// The wait ends on the dispatcher thread when the first walk is over already, so the work on the whole list moves to a pool thread.
		IReadOnlyList<QuickPickItem> items = await Task.Run(
				() => Arrange(snapshot: _tracker.Capture(), recentKeys, editors, cancellationToken),
				cancellationToken
			)
			.ConfigureAwait(false);

		_tracker.ThrowIfStopped();

		return items;
	}

	// Puts the recently picked files and the open documents first, as their own group, and leaves the other files in the order of the list.
	private IReadOnlyList<QuickPickItem> Arrange(
		WorkspaceFileSnapshot snapshot,
		IReadOnlyList<string> recentKeys,
		IReadOnlyList<OpenEditorDescriptor> editors,
		CancellationToken cancellationToken
	)
	{
		StringComparer comparer = _tracker.PathComparison.Comparer;
		Dictionary<string, OpenEditorDescriptor> openByPath = new(comparer);
		HashSet<string> seen = new(comparer);
		HashSet<int> recentIndexes = [];
		List<QuickPickItem> recent = [];

		foreach (OpenEditorDescriptor editor in editors)
		{
			if (editor is not null)
				openByPath.TryAdd(Normalize(editor.DocumentId), editor);
		}

		void AddRecent(string path)
		{
			if (!seen.Add(path))
				return;

			openByPath.TryGetValue(path, out OpenEditorDescriptor? editor);

			bool isUnsaved = editor?.IsUnsaved == true;

			string? unsavedText = isUnsaved ? _options.UnsavedText : null;
			string? iconKey = isUnsaved ? OpenEditorDescriptor.UnsavedIconKey : null;

			if (snapshot.IndexByPath.TryGetValue(path, out int index))
			{
				recentIndexes.Add(index);
				recent.Add(snapshot.Items[index] with { Group = RecentGroup, TrailingText = unsavedText, IconKey = iconKey });
			}
			else if (editor is not null)
			{
				recent.Add(ItemOfOpenEditor(path, editor, unsavedText, iconKey));
			}
		}

		foreach (string key in recentKeys)
			AddRecent(Normalize(key));

		foreach (OpenEditorDescriptor editor in editors)
		{
			if (editor is not null)
				AddRecent(Normalize(editor.DocumentId));
		}

		if (recent.Count == 0)
			return snapshot.Items;

		List<QuickPickItem> items = new(snapshot.Items.Length);

		items.AddRange(recent);

		for (int index = 0; index < snapshot.Items.Length; index++)
		{
			if ((index & 0x3FF) == 0)
				cancellationToken.ThrowIfCancellationRequested();

			if (!recentIndexes.Contains(index))
				items.Add(snapshot.Items[index]);
		}

		return items;
	}

	// A document outside the roots, or one that no walk met, is listed by its own path.
	private QuickPickItem ItemOfOpenEditor(string path, OpenEditorDescriptor editor, string? unsavedText, string? iconKey)
	{
		string name = Path.GetFileName(path);
		string folder = Path.GetDirectoryName(path) ?? string.Empty;

		return new QuickPickItem(path, name.Length == 0 ? editor.Title : name)
		{
			Description = folder.Length == 0 ? null : folder,
			Group = RecentGroup,
			TrailingText = unsavedText,
			IconKey = iconKey,
		};
	}

	// A host id that is not a path, such as the synthetic id of an untitled document, stays as it is.
	private static string Normalize(string id)
	{
		try
		{
			return Path.IsPathRooted(id) ? Path.GetFullPath(id) : id;
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
		{
			// An id that is no valid path cannot match a file, so it stays as it is and matches nothing.
			return id;
		}
	}
}
