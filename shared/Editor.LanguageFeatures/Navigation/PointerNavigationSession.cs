using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
using PointerModifiers = Avalonia.Input.KeyModifiers;
#else
using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.AvalonEdit.Threading;
using PointerModifiers = System.Windows.Input.ModifierKeys;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;

#endif

/// <summary>Navigates to the definition under each left click that holds the navigation modifiers, from attachment until disposal.</summary>
/// <remarks>
/// <para>
/// A click runs in order: the press, the modifier check, the hit test over text, the claim of the press, the asynchronous resolve,
/// and the navigation. The press is claimed in the routed handler, before anything awaits, so the editor's own press handler never sees it.
/// </para>
/// <para>
/// One navigation is in flight at a time. A later navigating click cancels the earlier one, and a document replacement
/// or disposal cancels the one in flight; after a replacement the session claims no press, because its requests carry a fixed document identifier.
/// A canceled navigation applies nothing, because the navigation checks its token in the same dispatcher turn as it applies a target.
/// Every member runs on the text area's thread, where the presses arrive.
/// </para>
/// </remarks>
internal sealed partial class PointerNavigationSession : IDisposable
{
	[LoggerMessage(
		EventId = 1040,
		EventName = "PointerNavigationFailed",
		Level = LogLevel.Warning,
		Message = "The definition navigation of a pointer click failed."
	)]
	private static partial void LogNavigationFailed(ILogger logger, Exception exception);

	private readonly TextArea _textArea;
	private readonly IDefinitionProvider _provider;
	private readonly string _documentId;
	private readonly Func<DefinitionLocation, CancellationToken, Task<bool>>? _tryNavigateCrossFileDefinitionAsync;
	private readonly PointerModifiers _modifiers;
	private readonly Func<Point, int?> _textOffsetFromPoint;
	private readonly ILogger _logger;
	private readonly IDisposable _subscription;

	// The cancellation of the navigation in flight, or null when none is; a later click, a document replacement or the disposal cancels it.
	private CancellationTokenSource? _navigation;

	// Set once the text area replaced its document: a request must never carry another document's text under the fixed identifier.
	private bool _isDocumentReplaced;

	/// <param name="textArea">The text area whose clicks navigate.</param>
	/// <param name="documentId">The identifier of the document the text area shows.</param>
	/// <param name="provider">The provider that resolves the definitions.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// The host callback that shows a definition in another document, or <see langword="null"/>.
	/// </param>
	/// <param name="modifiers">The modifier keys a navigating click holds, and no others.</param>
	/// <param name="textOffsetFromPoint">
	/// Maps a point in the text area to the offset of the text under it, or to <see langword="null"/> off the text;
	/// <see langword="null"/> selects <see cref="TextAreaExtensions.TryGetTextOffsetFromPoint"/>. A test replaces it instead of placing a real
	/// pointer over rendered text.
	/// </param>
	/// <param name="heldModifiers">
	/// Reads the modifier keys a press holds in place of the input, or <see langword="null"/> to read them from the input.
	/// A test replaces it, because it cannot hold a key of the real keyboard.
	/// </param>
	/// <param name="logger">The logger that receives a failed navigation, or <see langword="null"/>.</param>
	internal PointerNavigationSession(
		TextArea textArea,
		string documentId,
		IDefinitionProvider provider,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync,
		PointerModifiers modifiers,
		Func<Point, int?>? textOffsetFromPoint = null,
		Func<PointerModifiers>? heldModifiers = null,
		ILogger? logger = null
	)
	{
		_textArea = textArea;
		_provider = provider;
		_documentId = documentId;
		_tryNavigateCrossFileDefinitionAsync = tryNavigateCrossFileDefinitionAsync;
		_modifiers = modifiers;
		_textOffsetFromPoint = textOffsetFromPoint ?? (point => textArea.TryGetTextOffsetFromPoint(point, out int offset) ? offset : null);
		_logger = logger ?? NullLogger.Instance;
		_subscription = PointerNavigationSeam.SubscribePress(textArea, HandlePress, heldModifiers);
		textArea.DocumentChanged += HandleDocumentChanged;
	}

	/// <summary>The task of the latest navigation, which completes after its caret placement; a test awaits it.</summary>
	internal Task CurrentNavigation { get; private set; } = Task.CompletedTask;

	public void Dispose()
	{
		_textArea.DocumentChanged -= HandleDocumentChanged;
		_subscription.Dispose();
		_navigation?.Cancel();
	}

	// Claims a press that holds the modifiers over text and starts its navigation, which supersedes the one in flight. After a document replacement,
	// every press stays a plain click.
	private bool HandlePress(Point position, PointerModifiers modifiers)
	{
		if (_isDocumentReplaced || modifiers != _modifiers || _textOffsetFromPoint(position) is not { } offset)
			return false;

		_navigation?.Cancel();

		var navigation = new CancellationTokenSource();
		_navigation = navigation;

		CurrentNavigation = NavigateAsync(offset, navigation);
		return true;
	}

	// The requests carry the identifier of the document the session was attached for, so after a replacement it cancels the navigation that read the
	// earlier document and claims no press more.
	private void HandleDocumentChanged(object? sender, EventArgs e)
	{
		_isDocumentReplaced = true;
		_navigation?.Cancel();
	}

	private async Task NavigateAsync(int offset, CancellationTokenSource navigation)
	{
		TextDocument? document = _textArea.Document;
		ITextSourceVersion? version = document?.Version;
		int caretOffset = _textArea.Caret.Offset;
		bool navigated = false;

		try
		{
			navigated = await _textArea
				.GoToDefinitionAsync(
					documentId: _documentId,
					provider: _provider,
					offset,
					tryNavigateCrossFileDefinitionAsync: _tryNavigateCrossFileDefinitionAsync,
					cancellationToken: navigation.Token
				)
				.ConfigureAwait(true);
		}
		catch (OperationCanceledException) when (navigation.IsCancellationRequested) { }
		catch (Exception exception)
		{
			// The click has no caller to report to, so a failed provider or host callback is logged, and the click falls back to a plain click.
			LogNavigationFailed(_logger, exception);
		}

		// The fallback runs on the text area's thread, where a later click cancels this navigation, so the cancellation check and the caret
		// placement are one turn.
		await UiDispatcher
			.From(_textArea.Dispatcher)
			.InvokeAsync(() =>
			{
				if (!navigated && !navigation.IsCancellationRequested && IsUnchanged(document, version, caretOffset))
					PlaceCaret(offset);

				if (ReferenceEquals(_navigation, navigation))
					_navigation = null;

				navigation.Dispose();
			})
			.ConfigureAwait(true);
	}

	// A click that navigated nowhere places the caret where a plain click would, unless an edit or a caret move came after the press.
	private bool IsUnchanged(TextDocument? document, ITextSourceVersion? version, int caretOffset)
	{
		return document is not null
			&& version is not null
			&& TextAreaNavigationExtensions.IsCurrentSnapshot(_textArea, document, version)
			&& _textArea.Caret.Offset == caretOffset;
	}

	private void PlaceCaret(int offset)
	{
		_textArea.Focus();
		_textArea.ClearSelection();
		_textArea.Caret.Offset = offset;
	}
}
