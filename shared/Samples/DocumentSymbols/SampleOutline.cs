using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

namespace Nickelony.Samples;

/// <summary>Keeps the outline of one editor's document, which a document-symbol provider answers again after each edit settles.</summary>
/// <remarks>
/// The provider answers the whole tree, and the filter narrows it on this side, so typing in the filter box sends no request. A request gate keeps
/// only the newest request current.
/// </remarks>
internal sealed class SampleOutline : IDisposable
{
	private static readonly TimeSpan s_settleDelay = TimeSpan.FromMilliseconds(300);

	private readonly TextEditor _editor;
	private readonly IDocumentSymbolProvider? _provider;
	private readonly string _documentId;
	private readonly UiDispatcher _dispatcher = UiDispatcher.FromCurrent();
	private readonly LatestRequestGate _requests = new();

	private IReadOnlyList<DocumentSymbol> _symbols = [];
	private IDisposable? _pendingRefresh;
	private string _filter = string.Empty;

	/// <param name="editor">The editor whose document the outline describes.</param>
	/// <param name="provider">Answers the document-symbol requests, or <see langword="null"/> when the document has no outline.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	internal SampleOutline(TextEditor editor, IDocumentSymbolProvider? provider, string documentId)
	{
		_editor = editor;
		_provider = provider;
		_documentId = documentId;

		if (provider is null)
			return;

		editor.TextChanged += HandleTextChanged;
		_ = RefreshAsync();
	}

	/// <summary>Raised on the UI thread after <see cref="Symbols"/> changed.</summary>
	internal event EventHandler? Changed;

	/// <summary>The symbols of the last answer whose names contain the filter, with the ancestors that lead to them.</summary>
	internal IReadOnlyList<DocumentSymbol> Symbols => DocumentSymbolFilter.FilterByName(_symbols, _filter);

	/// <summary>The text a symbol name must contain, ignoring case, to stay in <see cref="Symbols"/>.</summary>
	internal string Filter
	{
		get => _filter;
		set
		{
			_filter = value;
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>Asks the provider for the symbols of the current text.</summary>
	internal async Task RefreshAsync()
	{
		if (_provider is not { } provider)
			return;

		await _requests.RunAsync(
			state: _editor.Text,

			computeAsync: (text, token) =>
				provider.GetDocumentSymbolsAsync(new DocumentSymbolRequest(_documentId, text, filterText: string.Empty), token),

			// An answer for an older text would point at the wrong ranges.
			canApply: (text, _) => string.Equals(text, _editor.Text, StringComparison.Ordinal),

			apply: symbols =>
			{
				_symbols = symbols;
				Changed?.Invoke(this, EventArgs.Empty);
			}
		);
	}

	public void Dispose()
	{
		_editor.TextChanged -= HandleTextChanged;
		_pendingRefresh?.Dispose();
		_requests.Invalidate();
	}

	private void HandleTextChanged(object? sender, EventArgs e)
	{
		_pendingRefresh?.Dispose();
		_pendingRefresh = _dispatcher.Schedule(s_settleDelay, () => _ = RefreshAsync());
	}
}
