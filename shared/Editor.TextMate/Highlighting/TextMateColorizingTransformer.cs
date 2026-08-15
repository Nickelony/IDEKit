#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows.Threading;
#endif
using TextMateSharp.Model;
using ModelRange = TextMateSharp.Model.Range;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Applies styles resolved from TextMate tokens to the editor's document lines as they are rendered.
/// The <see cref="TextMateThemeStyleResolver"/> translates each token's scopes into visual formatting.
/// </summary>
/// <remarks>
/// <para>
/// Colorizing runs on the UI thread while the editor builds visual lines, and the transformer never
/// tokenizes from the paint path. Lines the model has not tokenized yet render with the base style
/// until the model's background tokenizer completes them.
/// </para>
/// <para>
/// Token-change notifications can arrive from the model's tokenizer thread. The transformer coalesces
/// them into a single queued redraw scoped to the changed line range and redraws through the text
/// view's dispatcher. Add and remove the transformer on the UI thread that owns the view, because
/// mutating <see cref="TextView.LineTransformers"/> requires it. A redraw is skipped when the
/// transformer has been disposed or when the view's dispatcher has already shut down.
/// </para>
/// <para>
/// Coloring follows the model's background pass in document order; a region scrolled into view can stay
/// with the base style until the pass reaches it, because the transformer does not prioritize the
/// visible viewport.
/// </para>
/// <para>
/// Text decorations are applied with the editor's <c>SetTextDecorations</c> semantics, which union the
/// requested decorations with the decorations already present on the element. Hosts that stack several
/// colorizing transformers should account for that behavior.
/// </para>
/// <para>
/// Disposing removes the token-change listener but does not remove the transformer from
/// <see cref="TextView.LineTransformers"/>; the host owns that removal. Disposing through the owning
/// <see cref="TextMateHighlightingSession"/> also disposes the model, which stops TextMateSharp's
/// tokenizer thread and disposes the line list; a host that disposes a transformer on its own should
/// remove it from the view and dispose the model when no other listener needs it.
/// </para>
/// </remarks>
public sealed class TextMateColorizingTransformer : DocumentColorizingTransformer, IDisposable, IModelTokensChangedListener
{
	private readonly TMModel _model;
	private readonly TextMateThemeStyleResolver _styleResolver;
	private readonly TextMateTypefaceCache _typefaceCache = new();
	private readonly Func<Action, DispatcherOperation?> _queueRedraw;
	private readonly Action<int, int> _redrawRange;
	private readonly object _pendingRangeLock = new();
	private bool _isDisposed;

	// Coalescing gate: closed while a queued redraw has not run yet. A closed gate means the listener
	// only widens the pending range, so a burst of token-change notifications collapses into one redraw.
	// The gate is a small separate object so the queued operation's abort handler captures it rather
	// than the transformer, which would keep the transformer (and the model and text view) alive for
	// as long as the operation is rooted.
	private readonly RedrawGate _redrawGate = new();
	private int _pendingFirstLineIndex = int.MaxValue;
	private int _pendingLastLineIndex = -1;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateColorizingTransformer"/> class.
	/// </summary>
	/// <param name="textView">The text view to redraw when tokens change.</param>
	/// <param name="model">
	/// The TextMate model providing per-line tokens; it must tokenize the document rendered by
	/// <paramref name="textView"/>.
	/// </param>
	/// <param name="styleResolver">The resolver translating token scopes into visual styles.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/>, <paramref name="model"/>, or <paramref name="styleResolver"/> is
	/// <see langword="null"/>.
	/// </exception>
	public TextMateColorizingTransformer(TextView textView, TMModel model, TextMateThemeStyleResolver styleResolver)
		: this(
			textView,
			model,
			styleResolver,
			action => QueueRedraw(textView, action),
			(firstLineIndex, lastLineIndex) => RedrawLineRange(textView, firstLineIndex, lastLineIndex))
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateColorizingTransformer"/> class with an
	/// explicit redraw dispatch, so tests can observe the coalescing behavior that a real text view
	/// does not expose (<see cref="TextView.Redraw()"/> is not virtual). The public constructor
	/// validates the arguments it shares.
	/// </summary>
	/// <param name="textView">The text view to redraw when tokens change.</param>
	/// <param name="model">The TextMate model providing per-line tokens.</param>
	/// <param name="styleResolver">The resolver translating token scopes into visual styles.</param>
	/// <param name="queueRedraw">
	/// Queues a redraw action and returns the queued dispatcher operation, or <see langword="null"/> when
	/// the queue cannot report one; replaces the text view's dispatcher. A <see langword="null"/> return
	/// means the redraw cannot be tracked, so the coalescing gate is reopened immediately instead of
	/// waiting for the action to run.
	/// </param>
	/// <param name="redrawRange">
	/// Redraws an inclusive range of zero-based document lines; replaces the mapping to the text
	/// view's content. A range whose first index exceeds its last index requests no redraw.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/>, <paramref name="model"/>, <paramref name="styleResolver"/>,
	/// <paramref name="queueRedraw"/>, or <paramref name="redrawRange"/> is <see langword="null"/>.
	/// </exception>
	internal TextMateColorizingTransformer(
		TextView textView,
		TMModel model,
		TextMateThemeStyleResolver styleResolver,
		Func<Action, DispatcherOperation?> queueRedraw,
		Action<int, int> redrawRange)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(styleResolver);
		ArgumentNullException.ThrowIfNull(queueRedraw);
		ArgumentNullException.ThrowIfNull(redrawRange);

		_model = model;
		_styleResolver = styleResolver;
		_queueRedraw = queueRedraw;
		_redrawRange = redrawRange;

		// The listener is registered last so a notification never observes the transformer before the
		// redraw delegates the caller supplied are installed.
		_model.AddModelTokensChangedListener(this);
	}

	/// <inheritdoc/>
	protected override void ColorizeLine(DocumentLine line)
	{
		if (Volatile.Read(ref _isDisposed))
			return;

		int lineIndex = Math.Max(0, line.LineNumber - 1);
		List<TMToken> tokens = _model.GetLineTokens(lineIndex);

		// A line that is not tokenized yet stays with the base style for this paint. Forcing the
		// tokenization here would drive TextMateSharp's tokenizer from the paint thread while the
		// model's tokenizer thread may run the same tokenizer, which is not thread-safe; the model's
		// token-change notification queues a redraw once the background pass has tokenized the line.
		if (tokens is null || _model.IsLineInvalid(lineIndex))
			return;

		if (tokens.Count == 0)
			return;

		int lineLength = line.Length;

		for (int i = 0; i < tokens.Count; i++)
		{
			TMToken token = tokens[i];
			int startIndex = ClampToLine(token.StartIndex, lineLength);
			int endIndex = i + 1 < tokens.Count
				? ClampToLine(tokens[i + 1].StartIndex, lineLength)
				: lineLength;

			if (endIndex <= startIndex)
				continue;

			TextRunStyle style = _styleResolver.Resolve(token.Scopes);

			if (!style.HasFormatting)
				continue;

			int startOffset = line.Offset + startIndex;
			int endOffset = line.Offset + endIndex;

			ChangeLinePart(startOffset, endOffset, element => ApplyStyle(element, style));
		}
	}

	/// <summary>
	/// Stops listening for token changes from the TextMate model.
	/// The transformer is not removed from <see cref="TextView.LineTransformers"/>; the host owns that removal.
	/// </summary>
	public void Dispose()
	{
		if (Volatile.Read(ref _isDisposed))
			return;

		Volatile.Write(ref _isDisposed, true);
		_model.RemoveModelTokensChangedListener(this);
	}

	void IModelTokensChangedListener.ModelTokensChanged(ModelTokensChangedEvent e)
	{
		if (Volatile.Read(ref _isDisposed))
			return;

		// The pending range widens to cover every changed line of an unqueued burst; a notification
		// without ranges widens it to the whole document.
		AccumulatePendingRange(e.Ranges);

		// Coalesce bursts of token-change notifications into a single queued redraw. This listener may be
		// invoked from the model's tokenizer thread, so the gate must be thread-safe.
		if (!_redrawGate.TryClose())
			return;

		// Queue the redraw so it does not run while the editor is building visual lines.
		DispatcherOperation? operation;

		try
		{
			operation = _queueRedraw(() =>
			{
				_redrawGate.Open();

				if (Volatile.Read(ref _isDisposed))
					return;

				(int firstLineIndex, int lastLineIndex) = TakePendingRange();
				_redrawRange(firstLineIndex, lastLineIndex);
			});
		}
		catch (Exception exception) when (exception is InvalidOperationException or TaskCanceledException)
		{
			// The view's dispatcher can be unavailable or shut down when the host tears the view down
			// before disposing the transformer. Drop the redraw and reopen the gate; nothing repaints
			// after shutdown anyway, and the next notification would only fail again.
			_redrawGate.Open();
			return;
		}

		if (operation is not null)
		{
			// A dispatcher that shuts down after accepting the callback aborts it without running it,
			// which would leave the coalescing gate closed; reopening it on abort keeps later
			// notifications able to queue again. The status is re-checked after subscribing because an
			// operation can abort between being queued and the handler being attached, in which case the
			// event never fires. The handler captures the gate, not the transformer, so a long-lived
			// operation cannot root the transformer through its abort handler.
			RedrawGate gate = _redrawGate;
			operation.Aborted += (_, _) => gate.Open();

			if (operation.Status == DispatcherOperationStatus.Aborted)
				gate.Open();
		}
		else
		{
			// The queue could not report an operation, so nothing guarantees the action will run and
			// reopen the gate; reopen it now so later token-change notifications can still queue a
			// redraw instead of the view freezing on its last paint.
			_redrawGate.Open();
		}
	}

	private static int ClampToLine(int index, int lineLength)
		=> Math.Max(0, Math.Min(index, lineLength));

	/// <summary>
	/// Widens the pending redraw to cover the changed lines of one token-change notification.
	/// </summary>
	/// <param name="ranges">
	/// The changed line ranges reported by the model, or <see langword="null"/> (or an empty list) to
	/// request a whole-document redraw.
	/// </param>
	private void AccumulatePendingRange(List<ModelRange>? ranges)
	{
		int firstLineIndex = 0;
		int lastLineIndex = int.MaxValue;

		if (ranges is { Count: > 0 })
		{
			// Model ranges normally carry one-based line numbers, but TextMateSharp's SetGrammar also
			// emits zero-based ModelTokensChangedEvent ranges before the tokenizer switches to the
			// one-based registerChangedTokens notifications. A zero (or negative) line number is therefore
			// read as an absent lower bound and widens the redraw to the whole document instead of
			// producing a negative index that would under-cover it.
			firstLineIndex = int.MaxValue;
			lastLineIndex = 0;

			for (int i = 0; i < ranges.Count; i++)
			{
				int fromLineIndex = ranges[i].FromLineNumber - 1;

				if (fromLineIndex < 0)
				{
					firstLineIndex = 0;
					lastLineIndex = int.MaxValue;
					break;
				}

				firstLineIndex = Math.Min(firstLineIndex, fromLineIndex);
				lastLineIndex = Math.Max(lastLineIndex, ranges[i].ToLineNumber - 1);
			}
		}

		lock (_pendingRangeLock)
		{
			_pendingFirstLineIndex = Math.Min(_pendingFirstLineIndex, firstLineIndex);
			_pendingLastLineIndex = Math.Max(_pendingLastLineIndex, lastLineIndex);
		}
	}

	/// <summary>
	/// Reads and resets the pending redraw range.
	/// </summary>
	/// <returns>
	/// The inclusive zero-based line range to redraw, or an empty range when no notification reported
	/// changed lines.
	/// </returns>
	private (int FirstLineIndex, int LastLineIndex) TakePendingRange()
	{
		lock (_pendingRangeLock)
		{
			(int firstLineIndex, int lastLineIndex) = (_pendingFirstLineIndex, _pendingLastLineIndex);

			_pendingFirstLineIndex = int.MaxValue;
			_pendingLastLineIndex = -1;

			return (firstLineIndex, lastLineIndex);
		}
	}

	/// <summary>
	/// Queues a redraw action on the text view's dispatcher, returning the queued operation so the
	/// coalescing gate can observe its abortion.
	/// </summary>
	/// <param name="textView">The text view whose dispatcher queues the redraw.</param>
	/// <param name="action">The redraw action to queue.</param>
	/// <returns>The queued dispatcher operation.</returns>
	private static DispatcherOperation? QueueRedraw(TextView textView, Action action)
	{
#if AVALONIAEDIT
		return textView.Dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
#else
		return textView.Dispatcher.BeginInvoke(action);
#endif
	}

	/// <summary>
	/// Redraws the given inclusive range of document lines on the text view, clamped to the document
	/// that is current when the redraw runs.
	/// </summary>
	/// <param name="textView">The text view to redraw.</param>
	/// <param name="firstLineIndex">The zero-based index of the first line to redraw.</param>
	/// <param name="lastLineIndex">The zero-based index of the last line to redraw.</param>
	private static void RedrawLineRange(TextView textView, int firstLineIndex, int lastLineIndex)
	{
		TextDocument? document = textView.Document;

		if (document is null)
		{
			textView.Redraw();
			return;
		}

		if (ComputeRedrawRange(document, firstLineIndex, lastLineIndex) is { } range)
			RedrawTextRange(textView, range.Offset, range.Length);
	}

	/// <summary>
	/// Redraws a document offset range on the text view.
	/// </summary>
	/// <param name="textView">The text view to redraw.</param>
	/// <param name="offset">The document offset at which the redraw range starts.</param>
	/// <param name="length">The length of the redraw range.</param>
	private static void RedrawTextRange(TextView textView, int offset, int length)
	{
#if AVALONIAEDIT
		// AvaloniaEdit's range overload has no priority argument; the caller already runs on the UI
		// thread because the redraw was queued through the dispatcher.
		textView.Redraw(offset, length);
#else
		textView.Redraw(offset, length, DispatcherPriority.Normal);
#endif
	}

	/// <summary>
	/// Computes the document offset range that covers the given inclusive range of zero-based document
	/// lines, clamped to the document.
	/// </summary>
	/// <param name="document">The document to clamp against.</param>
	/// <param name="firstLineIndex">The zero-based index of the first line to redraw.</param>
	/// <param name="lastLineIndex">The zero-based index of the last line to redraw.</param>
	/// <returns>
	/// The offset and length to redraw, or <see langword="null"/> when the clamped range covers no
	/// lines.
	/// </returns>
	internal static (int Offset, int Length)? ComputeRedrawRange(TextDocument document, int firstLineIndex, int lastLineIndex)
	{
		int firstIndex = Math.Max(0, firstLineIndex);
		int lastIndex = Math.Min(lastLineIndex, document.LineCount - 1);

		if (lastIndex < firstIndex)
			return null;

		DocumentLine firstLine = document.GetLineByNumber(firstIndex + 1);
		DocumentLine lastLine = document.GetLineByNumber(lastIndex + 1);

		return (firstLine.Offset, lastLine.Offset + lastLine.TotalLength - firstLine.Offset);
	}

	private void ApplyStyle(VisualLineElement element, TextRunStyle style)
	{
		// The applier sets the typeface only when the style requests bold or italic text; the derived
		// typeface is resolved through the cache on that path only.
		if (style.IsBold || style.IsItalic)
		{
			TextRunStyleApplier.Apply(element, style, _typefaceCache.GetOrAdd(style, element.TextRunProperties.Typeface));
			return;
		}

		TextRunStyleApplier.Apply(element, style);
	}

	/// <summary>
	/// The coalescing gate: an interlocked flag that is closed while a queued redraw has not run yet.
	/// It is a separate object so a queued dispatcher operation's abort handler can capture the gate
	/// without capturing the owning transformer.
	/// </summary>
	private sealed class RedrawGate
	{
		private int _isClosed;

		/// <summary>
		/// Closes the gate when it was open.
		/// </summary>
		/// <returns>
		/// <see langword="true"/> when the gate was open and is now closed (the caller owns the
		/// redraw); <see langword="false"/> when it was already closed (a redraw is still pending).
		/// </returns>
		public bool TryClose()
			=> Interlocked.Exchange(ref _isClosed, 1) == 0;

		/// <summary>
		/// Opens the gate so the next token-change notification can queue a redraw.
		/// </summary>
		public void Open()
			=> Interlocked.Exchange(ref _isClosed, 0);
	}
}
