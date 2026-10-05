using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.TextMate;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate;

#endif

/// <summary>Owns the TextMate highlighting of one text view and the document it renders, from the first token to the removal of its colors.</summary>
/// <remarks>
/// <para>
/// <see cref="Start(TextView, IGrammar, TextMateTokenTheme, ILogger)"/> creates
/// an incremental line list over the document, the TextMateSharp <see cref="TMModel"/> bound to it, a style resolver for the theme, and a colorizer,
/// starts tokenization with the supplied grammar, and inserts the colorizer first among the view's <see cref="TextView.LineTransformers"/>.
/// The syntax colors are the bottom layer, so a transformer that colors over them, such as semantic tokens, keeps its colors whenever it was added.
/// </para>
/// <para>
/// The session is bound to the document it was started with, so following a document swap stays host policy: dispose the session and start a new
/// one for the new document instead of rebinding. <see cref="Theme"/> switches the colors without tokenizing the document again.
/// </para>
/// <para>
/// The session owns every part it composes and disposes them in the safe order, because a part disposed early desynchronizes the session.
/// Disposing stops the tokenizer: the colorizer is detached from the model first, and the model is disposed afterwards, which also disposes
/// the line list. The colorizer leaves the view's line transformers on the view's thread, posted there when the session is disposed
/// on another thread. The method is idempotent.
/// </para>
/// </remarks>
public sealed class TextMateHighlightingSession : IDisposable
{
	private readonly TextView _textView;
	private readonly ILogger? _logger;

	private TextMateTokenTheme _theme;
	private int _isDisposed;

	private TextMateHighlightingSession(
		TextView textView,
		TextMateDocumentLineList lineList,
		TMModel model,
		TextMateTokenTheme theme,
		TextMateThemeStyleResolver styleResolver,
		TextMateColorizer colorizer,
		ILogger? logger
	)
	{
		_textView = textView;
		_theme = theme;
		_logger = logger;
		LineList = lineList;
		Model = model;
		StyleResolver = styleResolver;
		Colorizer = colorizer;
	}

	/// <summary>The incremental line list that feeds the model and tracks the session's document.</summary>
	internal TextMateDocumentLineList LineList { get; }

	/// <summary>The model that tokenizes the session's document.</summary>
	internal TMModel Model { get; }

	/// <summary>The resolver that translates token scopes into the styles of <see cref="Theme"/>.</summary>
	internal TextMateThemeStyleResolver StyleResolver { get; private set; }

	/// <summary>The token theme that colors the view; a new one recolors it without tokenizing the document again.</summary>
	/// <exception cref="ArgumentNullException">The new theme is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the view.</exception>
	public TextMateTokenTheme Theme
	{
		get => _theme;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			UiDispatcher.From(_textView.Dispatcher).VerifyAccess();

			var styleResolver = new TextMateThemeStyleResolver(value, _logger);

			_theme = value;
			StyleResolver = styleResolver;
			Colorizer.ApplyStyleResolver(styleResolver);
			_textView.Redraw();
		}
	}

	/// <summary>The colorizer that applies the resolved styles while the view renders.</summary>
	internal TextMateColorizer Colorizer { get; }

	/// <summary>
	/// Creates the highlighting resources for a text view and the document it renders and starts tokenization with the supplied grammar.
	/// </summary>
	/// <param name="textView">The text view whose document is tokenized and which is redrawn when tokens change.</param>
	/// <param name="grammar">The grammar that tokenizes the document.</param>
	/// <param name="theme">The token theme that resolves token scopes into visual styles.</param>
	/// <param name="logger">
	/// An optional logger that reports invalid foreground colors, unsupported selectors, unrecognized font style traits, misplaced child combinators,
	/// and <see langword="null"/> theme rules.
	/// </param>
	/// <returns>The started session; the caller owns its disposal.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/>, <paramref name="grammar"/>, or <paramref name="theme"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns the view, or the view has no document; assign one before starting a session.
	/// </exception>
	/// <remarks>
	/// The method takes ownership of the resources it creates: when creating a resource or applying the grammar fails, the colorizer is detached
	/// and the model - which also disposes the line list - is disposed before the failure is rethrown, so a failed start leaves
	/// no tokenizer thread, model listener, or document subscription behind.
	/// </remarks>
	public static TextMateHighlightingSession Start(TextView textView, IGrammar grammar, TextMateTokenTheme theme, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(grammar);
		ArgumentNullException.ThrowIfNull(theme);

		return Start(textView, grammar, theme, logger, testHooks: null);
	}

	/// <summary>Creates the session and invokes the supplied test hook once the resources exist and before the grammar is applied.</summary>
	internal static TextMateHighlightingSession Start(
		TextView textView,
		IGrammar grammar,
		TextMateTokenTheme theme,
		ILogger? logger,
		TextMateHighlightingSessionTestHooks? testHooks
	)
	{
		UiDispatcher.From(textView.Dispatcher).VerifyAccess();

		TextDocument document = textView.Document ?? throw new InvalidOperationException("The text view has no document to tokenize.");

		var lineList = new TextMateDocumentLineList(document);

		TMModel? model = null;
		TextMateColorizer? colorizer = null;

		try
		{
			model = new TMModel(lineList);
			var styleResolver = new TextMateThemeStyleResolver(theme, logger);

			colorizer = new TextMateColorizer(textView, model, styleResolver);

			testHooks?.LineListCreated?.Invoke(lineList);

			// The colorizer registers its token-change listener while it is constructed, so the grammar
			// must start the tokenizer only afterwards; otherwise, the first notification could be raised
			// without a listener and the affected line would stay uncolored until the next change.
			model.SetGrammar(grammar);
			textView.LineTransformers.Insert(0, colorizer);

			return new TextMateHighlightingSession(textView, lineList, model, theme, styleResolver, colorizer, logger);
		}
		catch
		{
			// The resources are rolled back in the same order the session's own disposal uses: the
			// colorizer first, then the model, whose disposal also detaches the line list from the
			// document. A line list whose model was never created is disposed directly, and the original
			// failure is rethrown unchanged.
			colorizer?.Dispose();

			if (model is not null)
				model.Dispose();
			else
				lineList.Dispose();

			throw;
		}
	}

	/// <summary>
	/// Detaches the colorizer from the model, disposes the model, which also disposes the line list, and removes the colorizer from the view;
	/// the method is idempotent.
	/// </summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
			return;

		Colorizer.Dispose();
		Model.Dispose();

		// The line transformers belong to the view's thread, which a host may dispose the session off.
		var dispatcher = UiDispatcher.From(_textView.Dispatcher);

		if (dispatcher.CheckAccess())
			_textView.LineTransformers.Remove(Colorizer);
		else
			dispatcher.Post(() => _textView.LineTransformers.Remove(Colorizer));
	}
}
