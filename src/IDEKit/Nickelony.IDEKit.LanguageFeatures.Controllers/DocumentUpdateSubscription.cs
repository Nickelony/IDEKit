using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers;

/// <summary>Delivers the updates of one document from a push source to a callback on the UI thread, until it is disposed.</summary>
/// <remarks>
/// A source raises its event on any thread and for every document, so the subscription filters by document and posts to the dispatcher.
/// An update posted before disposal and run after it is dropped, so the callback never runs once <see cref="Dispose"/> returned on the UI thread.
/// </remarks>
internal sealed class DocumentUpdateSubscription<TItem> : IDisposable
{
	private readonly string _documentId;
	private readonly IEqualityComparer<string> _documentIdComparer;
	private readonly IUiDispatcher _dispatcher;
	private readonly Action<IReadOnlyList<TItem>> _onChanged;

	private Action? _unsubscribe;
	private volatile bool _isDisposed;

	internal DocumentUpdateSubscription(
		string documentId,
		IEqualityComparer<string> documentIdComparer,
		IUiDispatcher dispatcher,
		Action<IReadOnlyList<TItem>> onChanged
	)
	{
		_documentId = documentId;
		_documentIdComparer = documentIdComparer;
		_dispatcher = dispatcher;
		_onChanged = onChanged;
	}

	// The handler subscribes before the current items are read, so an update raised between the two is posted after nothing older than it.
	internal void Start(Action unsubscribe, IReadOnlyList<TItem> current)
	{
		_unsubscribe = unsubscribe;
		Post(current);
	}

	internal void HandleUpdated(string documentId, IReadOnlyList<TItem> items)
	{
		if (_documentIdComparer.Equals(documentId, _documentId))
			Post(items);
	}

	public void Dispose()
	{
		_isDisposed = true;
		Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
	}

	private void Post(IReadOnlyList<TItem> items)
	{
		if (_isDisposed)
			return;

		_dispatcher.Post(() =>
		{
			if (!_isDisposed)
				_onChanged(items);
		});
	}
}
