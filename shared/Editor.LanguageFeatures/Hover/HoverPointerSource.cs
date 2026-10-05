using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Hover;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;

#endif

/// <summary>Tracks the pointer over a text area and resolves it to a document offset for a <see cref="HoverController"/>.</summary>
/// <remarks>
/// <para>
/// The source listens to handled events too, so a child that consumes a pointer move cannot leave a stale position behind. It stores the position
/// and not the offset, because a scroll or an edit between two reads moves the offset under a pointer that did not move. Until the first move,
/// and after the pointer left, it reports no offset.
/// </para>
/// <para>
/// With a rest delay, a move rearms a debouncer that asks for a hover once the pointer rests, and the pointer leaving
/// asks at once, which finds no offset and hides the tooltip. A scroll of the text view under a pointer that rests over the text rearms the debouncer
/// as a move does, because the pointer then addresses other text. A move to the position the pointer already holds, and a scroll event that leaves
/// the offset where it was, rearm nothing: a platform reports such a move when a window under a resting pointer appears or closes, as a hover popup
/// that typing hid does, and a rearm would bring the hidden hover back.
/// </para>
/// </remarks>
internal sealed class HoverPointerSource : IHoverPointerSource
{
	private readonly IDisposable _subscription;
	private readonly TextArea _textArea;
	private readonly Func<Point, int?> _offsetFromPoint;
	private readonly UiDispatcher? _dispatcher;
	private readonly DispatcherDebouncer? _rest;
	private readonly Action? _requestHover;

	// The latest pointer position over the text area in its coordinate space, or null when the pointer left it or has not moved over it yet.
	// The pointer events write it and the controller reads it on the text area's thread; Dispose clears it on whichever thread disposes the
	// controller.
	private Point? _pointerPosition;

	// The document whose changes the version counts, which follows each document that replaces the text area's document.
	private TextDocument? _document;

	// The scroll offset the last scroll event reported, so an event that moved nothing rearms nothing.
	private Vector _scrollOffset;

	/// <param name="textArea">The text area whose pointer the source tracks.</param>
	/// <param name="offsetFromPoint">
	/// Maps a point in the text area to a document offset, or to <see langword="null"/> for a point that addresses no document position.
	/// The source calls it on every read, so a test replaces it instead of placing a real pointer over rendered text.
	/// </param>
	/// <param name="restDelay">
	/// How long the pointer rests before <paramref name="requestHover"/> runs, or <see langword="null"/> to never run it.
	/// </param>
	/// <param name="requestHover">Asks the controller for a hover, or <see langword="null"/> when the host asks itself.</param>
	internal HoverPointerSource(TextArea textArea, Func<Point, int?> offsetFromPoint, TimeSpan? restDelay = null, Action? requestHover = null)
	{
		_offsetFromPoint = offsetFromPoint;
		_textArea = textArea;

		if (restDelay is { } delay && requestHover is not null)
		{
			_dispatcher = UiDispatcher.From(textArea.Dispatcher);
			_rest = new DispatcherDebouncer(delay, _dispatcher);
			_requestHover = requestHover;
			_scrollOffset = textArea.TextView.ScrollOffset;
			textArea.TextView.ScrollOffsetChanged += HandleScrolled;
		}

		_subscription = HoverSeam.SubscribePointer(textArea, HandleMoved, HandleLeft);

		_document = textArea.Document;

		if (_document is not null)
			_document.TextChanged += HandleDocumentTextChanged;

		textArea.DocumentChanged += HandleTextAreaDocumentChanged;
	}

	/// <summary>A counter that every change of the text area's text, and every document that replaces the text area's document, advances.</summary>
	/// <remarks>
	/// <see cref="HoverControllerFactory"/> passes it as the controller's context version when the host supplies none, so a hover whose document
	/// changed while it was in flight shows nothing.
	/// </remarks>
	internal int DocumentVersion { get; private set; }

	/// <inheritdoc/>
	public int? GetHoveredOffset() => _pointerPosition is { } pointerPosition ? _offsetFromPoint(pointerPosition) : null;

	/// <inheritdoc/>
	public void Dispose()
	{
		_pointerPosition = null;

		_subscription.Dispose();

		if (_requestHover is not null)
			_textArea.TextView.ScrollOffsetChanged -= HandleScrolled;

		_textArea.DocumentChanged -= HandleTextAreaDocumentChanged;

		if (_document is not null)
			_document.TextChanged -= HandleDocumentTextChanged;

		// The debouncer's timer belongs to the text area's thread, which the controller may not be disposed on.
		if (_rest is not null && _dispatcher is not null)
		{
			if (_dispatcher.CheckAccess())
				_rest.Dispose();
			else
				_dispatcher.Post(_rest.Dispose);
		}
	}

	private void HandleMoved(Point position)
	{
		if (_pointerPosition == position)
			return;

		_pointerPosition = position;

		if (_requestHover is not null)
			_rest?.Arm(_requestHover);
	}

	private void HandleScrolled(object? sender, EventArgs e)
	{
		Vector scrollOffset = _textArea.TextView.ScrollOffset;

		if (scrollOffset == _scrollOffset)
			return;

		_scrollOffset = scrollOffset;

		if (_pointerPosition is not null && _requestHover is not null)
			_rest?.Arm(_requestHover);
	}

	private void HandleLeft()
	{
		_pointerPosition = null;

		_rest?.Cancel();
		_requestHover?.Invoke();
	}

	private void HandleDocumentTextChanged(object? sender, EventArgs e) => DocumentVersion++;

	private void HandleTextAreaDocumentChanged(object? sender, EventArgs e)
	{
		if (_document is not null)
			_document.TextChanged -= HandleDocumentTextChanged;

		_document = _textArea.Document;

		if (_document is not null)
			_document.TextChanged += HandleDocumentTextChanged;

		DocumentVersion++;
	}
}
