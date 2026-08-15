#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
#endif
using Microsoft.Extensions.Logging;
using TextMateSharp.Grammars;
using TextMateSharp.Model;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Owns the resources of one TextMate highlighting setup for a text view and one document.
/// </summary>
/// <remarks>
/// <para>
/// The session takes ownership of the resources it creates; when their creation or the grammar
/// application fails, <see cref="Start(TextView, IGrammar, TextMateTokenTheme, ILogger)"/> disposes them
/// again before the failure is rethrown.
/// </para>
/// <para>
/// <see cref="Start(TextView, IGrammar, TextMateTokenTheme, ILogger)"/> creates the incremental
/// <see cref="TextMateDocumentLineList"/>, the
/// <see cref="TMModel"/> bound to it, the <see cref="TextMateThemeStyleResolver"/>, and the
/// <see cref="TextMateColorizingTransformer"/> wired to the view, and starts tokenization with the
/// supplied grammar. The session never installs the transformer: add <see cref="Transformer"/> to
/// <see cref="TextView.LineTransformers"/> to colorize the view, and remove it again when the view
/// stops using the session. Add and remove the transformer on the UI thread that owns the view,
/// because mutating <see cref="TextView.LineTransformers"/> requires it.
/// </para>
/// <para>
/// The session is bound to the document it was started with, so following a document swap stays host
/// policy: dispose the session and start a new one for the new document instead of rebinding. The
/// style resolver is exposed so a host can share the same theme and cache with views that do not use
/// the session.
/// </para>
/// <para>
/// Disposing stops the tokenizer in the safe order: the transformer is detached from the model first,
/// and the model is disposed afterwards, which also disposes the line list. The method is idempotent
/// and does not touch <see cref="TextView.LineTransformers"/>, so remove the transformer from the view
/// when it is still alive.
/// </para>
/// </remarks>
public sealed class TextMateHighlightingSession : IDisposable
{
	private bool _isDisposed;

	private TextMateHighlightingSession(
		TextMateDocumentLineList lineList,
		TMModel model,
		TextMateThemeStyleResolver styleResolver,
		TextMateColorizingTransformer transformer)
	{
		LineList = lineList;
		Model = model;
		StyleResolver = styleResolver;
		Transformer = transformer;
	}

	/// <summary>
	/// Gets the incremental line list that feeds the model and tracks the session's document.
	/// </summary>
	public TextMateDocumentLineList LineList { get; }

	/// <summary>
	/// Gets the model that tokenizes the session's document.
	/// </summary>
	public TMModel Model { get; }

	/// <summary>
	/// Gets the resolver that translates token scopes into visual styles.
	/// </summary>
	public TextMateThemeStyleResolver StyleResolver { get; }

	/// <summary>
	/// Gets the transformer that applies the resolved styles while the view renders.
	/// </summary>
	public TextMateColorizingTransformer Transformer { get; }

	/// <summary>
	/// Creates the highlighting resources for a text view and the document it renders and starts
	/// tokenization with the supplied grammar.
	/// </summary>
	/// <remarks>
	/// The method takes ownership of the resources it creates: when creating a resource or applying the
	/// grammar fails, the transformer is detached and the model - which also disposes the line list - is
	/// disposed before the failure is rethrown, so a failed start leaves no tokenizer thread, model
	/// listener, or document subscription behind.
	/// </remarks>
	/// <param name="textView">
	/// The text view whose document is tokenized and which is redrawn when tokens change.
	/// </param>
	/// <param name="grammar">The grammar used to tokenize the document.</param>
	/// <param name="theme">The token theme that resolves token scopes into visual styles.</param>
	/// <param name="logger">
	/// An optional logger used to report invalid foreground colors, unsupported selectors,
	/// unrecognized font style traits, misplaced child combinators, and null theme rules.
	/// </param>
	/// <returns>The started session; the caller owns its disposal.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/>, <paramref name="grammar"/>, or <paramref name="theme"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The text view has no document; assign one before starting a session.
	/// </exception>
	public static TextMateHighlightingSession Start(
		TextView textView,
		IGrammar grammar,
		TextMateTokenTheme theme,
		ILogger? logger = null)
		=> Start(textView, grammar, theme, logger, testHooks: null);

	/// <summary>
	/// The test-only construction path of <see cref="Start(TextView, IGrammar, TextMateTokenTheme, ILogger?)"/>:
	/// creates the session and invokes the supplied test hook once the resources exist and before the grammar
	/// is applied.
	/// </summary>
	internal static TextMateHighlightingSession Start(
		TextView textView,
		IGrammar grammar,
		TextMateTokenTheme theme,
		ILogger? logger,
		TextMateHighlightingSessionTestHooks? testHooks)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(grammar);
		ArgumentNullException.ThrowIfNull(theme);

		TextDocument document = textView.Document
			?? throw new InvalidOperationException("The text view has no document to tokenize.");

		var lineList = new TextMateDocumentLineList(document);
		TMModel? model = null;
		TextMateColorizingTransformer? transformer = null;

		try
		{
			model = new TMModel(lineList);
			var styleResolver = new TextMateThemeStyleResolver(theme, logger);

			transformer = new TextMateColorizingTransformer(textView, model, styleResolver);

			testHooks?.LineListCreated?.Invoke(lineList);

			// The transformer registers its token-change listener while it is constructed, so the grammar
			// must start the tokenizer only afterwards; otherwise, the first notification could be raised
			// without a listener and the affected line would stay uncolored until the next change.
			model.SetGrammar(grammar);

			return new TextMateHighlightingSession(lineList, model, styleResolver, transformer);
		}
		catch
		{
			// The resources are rolled back in the same order the session's own disposal uses: the
			// transformer first, then the model, whose disposal also detaches the line list from the
			// document. A line list whose model was never created is disposed directly, and the original
			// failure is rethrown unchanged.
			transformer?.Dispose();

			if (model is not null)
				model.Dispose();
			else
				lineList.Dispose();

			throw;
		}
	}

	/// <summary>
	/// Detaches the transformer from the model and disposes the model, which also disposes the line
	/// list. The method is idempotent.
	/// </summary>
	public void Dispose()
	{
		if (Volatile.Read(ref _isDisposed))
			return;

		Volatile.Write(ref _isDisposed, true);

		Transformer.Dispose();
		Model.Dispose();
	}
}
