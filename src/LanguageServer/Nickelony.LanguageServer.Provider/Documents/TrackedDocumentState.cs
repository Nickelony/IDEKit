namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Stores the neutral mirrored state for a tracked language-server document.
/// </summary>
/// <remarks>
/// Individual property reads and mutations are atomic, but a multi-field view (for example the path/URI pair
/// during a rename) is only consistent through <see cref="CreateSnapshot"/>. Derived types should still
/// synchronize any additional mutable state they introduce.
/// </remarks>
public abstract class TrackedDocumentState
{
	private readonly object _stateSyncRoot = new();

	private string _filePath;
	private string _uri;
	private string _content;
	private int _version;
	private bool _isOpen;
	private long _lastAccessStamp;

	/// <summary>
	/// Initializes a new instance of the <see cref="TrackedDocumentState"/> class from an initial-state payload.
	/// </summary>
	/// <param name="initialState">The initial tracked-document state.</param>
	/// <exception cref="ArgumentNullException">
	/// <see cref="TrackedDocumentInitialState.FilePath"/>, <see cref="TrackedDocumentInitialState.Uri"/>, or
	/// <see cref="TrackedDocumentInitialState.Content"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <see cref="TrackedDocumentInitialState.FilePath"/> or <see cref="TrackedDocumentInitialState.Uri"/> is empty
	/// or whitespace-only.
	/// </exception>
	public TrackedDocumentState(TrackedDocumentInitialState initialState)
	{
		ArgumentNullException.ThrowIfNull(initialState.FilePath);
		ArgumentNullException.ThrowIfNull(initialState.Uri);
		ArgumentNullException.ThrowIfNull(initialState.Content);
		ArgumentException.ThrowIfNullOrWhiteSpace(initialState.FilePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(initialState.Uri);

		_filePath = initialState.FilePath;
		_uri = initialState.Uri;
		_content = initialState.Content;
		_version = initialState.Version;
		_isOpen = initialState.IsOpen;
		_lastAccessStamp = initialState.LastAccessStamp;

		References = new DocumentReferenceTracker(initialState.OpenReferenceCount, initialState.RequestReferenceCount);
	}

	/// <summary>
	/// Gets the normalized file path.
	/// </summary>
	public string FilePath
	{
		get
		{
			lock (_stateSyncRoot)
				return _filePath;
		}
	}

	/// <summary>
	/// Gets the file URI mirrored to the server.
	/// </summary>
	public string Uri
	{
		get
		{
			lock (_stateSyncRoot)
				return _uri;
		}
	}

	/// <summary>
	/// Gets the latest synchronized content.
	/// </summary>
	public string Content
	{
		get
		{
			lock (_stateSyncRoot)
				return _content;
		}
	}

	/// <summary>
	/// Gets the current tracked version.
	/// </summary>
	public int Version
	{
		get
		{
			lock (_stateSyncRoot)
				return _version;
		}
	}

	/// <summary>
	/// Gets a value indicating whether the server currently considers the document open.
	/// </summary>
	public bool IsOpen
	{
		get
		{
			lock (_stateSyncRoot)
				return _isOpen;
		}
	}

	/// <summary>
	/// Gets the access stamp used for idle-document eviction ordering.
	/// </summary>
	public long LastAccessStamp
	{
		get
		{
			lock (_stateSyncRoot)
				return _lastAccessStamp;
		}
	}

	/// <summary>
	/// Gets the active ownership references for the document.
	/// </summary>
	public DocumentReferenceTracker References { get; }

	/// <summary>
	/// Creates a stable snapshot of the current tracked document state.
	/// </summary>
	/// <returns>The current document snapshot.</returns>
	public DocumentSnapshot CreateSnapshot()
	{
		lock (_stateSyncRoot)
			return new(_filePath, _uri, _content, _version);
	}

	/// <summary>
	/// Updates the access stamp used for idle-document eviction ordering.
	/// </summary>
	/// <param name="lastAccessStamp">The new access stamp.</param>
	protected void SetLastAccessStamp(long lastAccessStamp)
	{
		lock (_stateSyncRoot)
			_lastAccessStamp = lastAccessStamp;
	}

	/// <summary>
	/// Reopens the tracked server document with fresh content and a new version.
	/// </summary>
	/// <param name="content">The reopened content.</param>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	protected void ReopenDocument(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		lock (_stateSyncRoot)
		{
			_content = content;
			_version++;
			_isOpen = true;
		}
	}

	/// <summary>
	/// Replaces the tracked content and advances the version.
	/// </summary>
	/// <param name="content">The replacement content.</param>
	/// <returns>The previous content snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	protected string ReplaceContent(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		lock (_stateSyncRoot)
		{
			string previousContent = _content;
			_content = content;
			_version++;

			return previousContent;
		}
	}

	/// <summary>
	/// Replaces the tracked path and URI after a rename.
	/// </summary>
	/// <param name="filePath">The normalized replacement file path.</param>
	/// <param name="uri">The replacement file URI.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="uri"/> is <see langword="null"/>.
	/// </exception>
	protected void RenameDocument(string filePath, string uri)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(uri);

		lock (_stateSyncRoot)
		{
			_filePath = filePath;
			_uri = uri;
		}
	}

	/// <summary>
	/// Marks the locally mirrored server state as closed without contacting the server.
	/// </summary>
	protected void MarkDocumentClosed()
	{
		lock (_stateSyncRoot)
			_isOpen = false;
	}
}
