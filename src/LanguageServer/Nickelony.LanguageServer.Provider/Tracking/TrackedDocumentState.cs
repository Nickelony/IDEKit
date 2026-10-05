namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Stores the mirrored state of one tracked language-server document: its path, content, version, leases and cached server payloads.
/// </summary>
/// <remarks>
/// <para>
/// Individual property reads and mutations are atomic, but a multi-field view (for example the path/URI pair during a rename) is only consistent
/// through <see cref="CreateSnapshot"/>.
/// </para>
/// <para>
/// The payload caches are read and written only under the lock of the owning <see cref="DocumentTracker"/>, so they need no synchronization
/// of their own.
/// </para>
/// </remarks>
internal sealed class TrackedDocumentState
{
	private readonly object _stateSyncRoot = new();

	private string _filePath;
	private string _uri;
	private string _documentId;
	private string _content;

	private int _version;
	private bool _isOpen;
	private long _lastAccessStamp;

	// One cache per feature slot, created on first use.
	private readonly Dictionary<object, IPayloadCache> _payloadCaches = [];

	/// <summary>Initializes a new instance of the <see cref="TrackedDocumentState"/> class from an initial-state payload.</summary>
	/// <param name="initialState">The initial tracked-document state.</param>
	internal TrackedDocumentState(TrackedDocumentDescriptor initialState)
	{
		_filePath = initialState.FilePath;
		_uri = initialState.Uri;
		_documentId = initialState.DocumentId ?? initialState.FilePath;
		_content = initialState.Content;
		_version = initialState.Version;
		_isOpen = initialState.IsOpen;
		_lastAccessStamp = initialState.LastAccessStamp;

		Leases = new DocumentLeaseCounts(initialState.OpenLeaseCount, initialState.RequestLeaseCount);
	}

	/// <summary>The normalized file path.</summary>
	internal string FilePath
	{
		get
		{
			lock (_stateSyncRoot)
				return _filePath;
		}
	}

	/// <summary>The file URI mirrored to the server.</summary>
	internal string Uri
	{
		get
		{
			lock (_stateSyncRoot)
				return _uri;
		}
	}

	/// <summary>The id the host supplied when it created, last opened or renamed the record.</summary>
	internal string DocumentId
	{
		get
		{
			lock (_stateSyncRoot)
				return _documentId;
		}
	}

	/// <summary>The latest synchronized content.</summary>
	internal string Content
	{
		get
		{
			lock (_stateSyncRoot)
				return _content;
		}
	}

	internal int Version
	{
		get
		{
			lock (_stateSyncRoot)
				return _version;
		}
	}

	/// <summary>Indicates whether the server currently considers the document open.</summary>
	internal bool IsOpen
	{
		get
		{
			lock (_stateSyncRoot)
				return _isOpen;
		}
	}

	/// <summary>The access stamp used for idle-document eviction ordering.</summary>
	internal long LastAccessStamp
	{
		get
		{
			lock (_stateSyncRoot)
				return _lastAccessStamp;
		}
	}

	/// <summary>The active ownership leases for the document.</summary>
	internal DocumentLeaseCounts Leases { get; }

	/// <summary>Creates a stable snapshot of the current tracked document state.</summary>
	/// <returns>The current document snapshot.</returns>
	internal DocumentSnapshot CreateSnapshot()
	{
		lock (_stateSyncRoot)
			return new(filePath: _filePath, uri: _uri, content: _content, version: _version, documentId: _documentId);
	}

	/// <summary>Takes the id a host opened the document with, so the events raised for it from now on carry that id.</summary>
	/// <param name="documentId">The id the host supplied to the open.</param>
	internal void AdoptDocumentId(string documentId)
	{
		lock (_stateSyncRoot)
			_documentId = documentId;
	}

	/// <summary>Returns the payload cache a feature keeps in this document, creating it on first use.</summary>
	/// <typeparam name="TCache">The cache type of the slot.</typeparam>
	/// <param name="slot">The slot that names the feature's cache.</param>
	/// <returns>The cache of the slot.</returns>
	internal TCache GetPayloadCache<TCache>(PayloadSlot<TCache> slot)
		where TCache : class, IPayloadCache, new()
	{
		if (_payloadCaches.TryGetValue(slot, out IPayloadCache? existing))
			return (TCache)existing;

		var created = new TCache();
		_payloadCaches[slot] = created;

		return created;
	}

	/// <summary>Drops every cached payload because the cached payloads describe content that is gone.</summary>
	internal void ClearPayloadCaches()
	{
		foreach (IPayloadCache cache in _payloadCaches.Values)
			cache.Clear();
	}

	/// <summary>Drops the version stamp of every payload cache and keeps the payloads.</summary>
	internal void ResetPayloadVersionStamps()
	{
		foreach (IPayloadCache cache in _payloadCaches.Values)
			cache.ResetVersionStamp();
	}

	/// <summary>Updates the access stamp used for idle-document eviction ordering.</summary>
	/// <param name="lastAccessStamp">The new access stamp.</param>
	/// <remarks>
	/// Stamps must only advance while the tracker lock is held: idle documents are sorted by stamp under that lock during trimming,
	/// and out-of-lock advances can violate the sort's comparer contract.
	/// </remarks>
	internal void SetLastAccessStamp(long lastAccessStamp)
	{
		lock (_stateSyncRoot)
			_lastAccessStamp = lastAccessStamp;
	}

	/// <summary>Reopens the tracked server document with fresh content and a new version.</summary>
	/// <param name="content">The reopened content.</param>
	internal void ReopenDocument(string content)
	{
		lock (_stateSyncRoot)
		{
			_content = content;
			_version++;
			_isOpen = true;
		}
	}

	/// <summary>Replaces the tracked content and advances the version.</summary>
	/// <param name="content">The replacement content.</param>
	/// <returns>The content tracked before the replacement.</returns>
	internal string ReplaceContent(string content)
	{
		lock (_stateSyncRoot)
		{
			string previousContent = _content;
			_content = content;
			_version++;

			return previousContent;
		}
	}

	/// <summary>Replaces the tracked path, URI and host id after a rename.</summary>
	/// <param name="filePath">The normalized replacement file path.</param>
	/// <param name="uri">The replacement file URI.</param>
	/// <param name="documentId">
	/// The id the host renamed the document to, or <see langword="null"/> when it equals <paramref name="filePath"/>.
	/// </param>
	internal void RenameDocument(string filePath, string uri, string? documentId = null)
	{
		lock (_stateSyncRoot)
		{
			_filePath = filePath;
			_uri = uri;
			_documentId = documentId ?? filePath;
		}
	}

	/// <summary>Marks the locally mirrored server state as closed without contacting the server.</summary>
	internal void MarkDocumentClosed()
	{
		lock (_stateSyncRoot)
			_isOpen = false;
	}
}
