using Nickelony.IDEKit.Core.Notifications;
#if AVALONIAEDIT
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Threading;
using TextInputEventArgs = System.Windows.Input.TextCompositionEventArgs;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Editing;

#endif

/// <summary>Reports each character typed into a text area once its input applied, whichever input handler applied it.</summary>
/// <remarks>
/// <para>
/// The subscription watches the text area's preview stage of text input, handled input included, and its <c>TextEntering</c> stage,
/// which programmatic input reaches without the preview stage. The first stage of an input captures the document version and the caret,
/// and the report is posted to the dispatcher, so it runs after every handler of that input: the completion window's commit, an auto-closing insert
/// and an overtype skip apply first.
/// </para>
/// <para>
/// The report runs only when the input changed the document or moved the caret, and the character before the caret
/// is the typed one, so input that a handler consumed or replaced reports nothing, and so does a wrap of a selection, which leaves the caret
/// after the wrapped text. Input of more than one character, such as a composition result, reports nothing. Inputs that arrive before the post
/// runs report once, for the last character. The subscription must be created and disposed on the text area's thread.
/// </para>
/// <para>
/// Programmatic input, such as <c>PerformTextInput</c>, reaches no preview stage, so its baseline is taken on <c>TextEntering</c>.
/// A <c>TextEntering</c> handler subscribed before the subscription that applies the input, as an auto-closing service does, changes the document
/// before that capture, so such input reports nothing; neither engine raises an earlier stage for it.
/// </para>
/// </remarks>
internal sealed class TypedTextSubscription : IDisposable
{
	private readonly TextArea _textArea;
	private readonly UiDispatcher _dispatcher;
	private readonly Action<char, int> _typed;
	private readonly ActionSubscription _previewStage;

	private (TextDocument Document, ITextSourceVersion Version, int CaretOffset, char Character)? _pending;
	private bool _isDisposed;

	/// <param name="textArea">The text area whose typed text is reported.</param>
	/// <param name="typed">Receives the typed character and the caret offset after it.</param>
	internal TypedTextSubscription(TextArea textArea, Action<char, int> typed)
	{
		_textArea = textArea;
		_typed = typed;
		_dispatcher = UiDispatcher.From(textArea.Dispatcher);
		_previewStage = SubscribePreviewStage(textArea, Capture);

		textArea.TextEntering += HandleTextEntering;
	}

	/// <summary>Removes the input subscriptions; a report already posted does nothing.</summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_pending = null;
		_previewStage.Dispose();
		_textArea.TextEntering -= HandleTextEntering;
	}

	// The preview stage runs before the text area's own handling and before any TextEntering subscriber, such as an auto-closing service that
	// applies the input and marks it handled, so the capture sees the document as it was before the input.
	private static ActionSubscription SubscribePreviewStage(TextArea textArea, Action<string?> capture)
	{
#if AVALONIAEDIT
		void handler(object? _, TextInputEventArgs e) => capture(e.Text);
		textArea.AddHandler(routedEvent: InputElement.TextInputEvent, handler, routes: RoutingStrategies.Tunnel, handledEventsToo: true);
		return new ActionSubscription(() => textArea.RemoveHandler(InputElement.TextInputEvent, handler));
#else
		TextCompositionEventHandler handler = (_, e) => capture(e.Text);
		textArea.AddHandler(UIElement.PreviewTextInputEvent, handler, handledEventsToo: true);
		return new ActionSubscription(() => textArea.RemoveHandler(UIElement.PreviewTextInputEvent, handler));
#endif
	}

	private void HandleTextEntering(object? sender, TextInputEventArgs e) => Capture(e.Text);

	private void Capture(string? text)
	{
		if (_isDisposed || text is not { Length: 1 } || _textArea.Document is not { } document)
			return;

		// A second stage of the same input, or an input that arrives before the post runs, keeps the first capture as the baseline.
		if (_pending is { } pending)
		{
			_pending = pending with { Character = text[0] };
			return;
		}

		_pending = (document, document.Version, _textArea.Caret.Offset, text[0]);
		_dispatcher.Post(Report);
	}

	private void Report()
	{
		if (_isDisposed || _pending is not { } pending)
			return;

		_pending = null;

		if (!ReferenceEquals(_textArea.Document, pending.Document))
			return;

		TextDocument document = pending.Document;
		int caretOffset = _textArea.Caret.Offset;
		bool isApplied = pending.Version.CompareAge(document.Version) != 0 || caretOffset != pending.CaretOffset;

		if (!isApplied || caretOffset <= 0 || caretOffset > document.TextLength || document.GetCharAt(caretOffset - 1) != pending.Character)
			return;

		_typed(pending.Character, caretOffset);
	}
}
