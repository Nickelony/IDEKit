using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;

#endif

/// <summary>Applies resolved semantic-token styles while the editor renders the text view.</summary>
/// <remarks>
/// <para>
/// The host adds the colorizer to the text view's line transformers and must remove it again before the view
/// it was registered for is torn down (for example when the editor is closed or the view is replaced); the colorizer itself registers
/// no event handlers. <see cref="ApplyTokens"/>, <see cref="Rebuild"/>, and <see cref="ClearTokens"/> redraw the text view and must be called
/// on the UI thread; each verifies the caller's access and throws <see cref="InvalidOperationException"/> off that thread.
/// </para>
/// <para>
/// A push, rebuild, or clear redraws the whole text view rather than an offset range, because a new token set can restyle any line.
/// </para>
/// <para>
/// A token set is anchored to the document when it is applied, so a styled span follows the edits made after it until the next
/// set replaces it: an insertion before a token moves its color with its text, an insertion inside it widens it, a character typed at its end
/// keeps the base color until the next set, and a deletion shrinks it. A document replaced in the view shows no styling until the next push.
/// Each token is drawn on the line that contains its start offset, clipped to that line, so a token that spans several lines is drawn on its start
/// line only.
/// </para>
/// <para>
/// The style resolver is invoked synchronously while the styled spans are built; a resolver that throws aborts the current token application
/// with the exception escaping to the caller. The applied token set and its styling remain in effect, so the push can be retried after the resolver
/// configuration is fixed.
/// </para>
/// <para>
/// Styled tokens on the same line are applied in ascending order of start offset, then length, then position in the pushed set, so where two tokens
/// overlap the one that comes later in that order is applied over the earlier one.
/// </para>
/// </remarks>
public sealed class SemanticTokensColorizer : DocumentColorizingTransformer
{
	private static readonly Comparison<StyledToken> s_paintOrder = ComparePaintOrder;

	private readonly TextView _textView;
	private readonly ISemanticTokenStyleResolver _styleResolver;

	// The spans of one line in paint order, reused by every line the render pass colorizes.
	private readonly List<StyledToken> _lineTokens = [];

	// The typefaces that bold and italic styles derive from the base typefaces of the elements they paint.
	private readonly DerivedTypefaceCache _typefaceCache = new();

	// The applied token set in pushed order; null stands for no applied tokens.
	private SemanticToken[]? _tokens;

	// The document and its version when the set was applied. Together with the token sequence they form the identity the repeat-push
	// check compares against; a null document is a valid value (applied while detached).
	private TextDocument? _styledDocument;
	private ITextSourceVersion? _styledVersion;

	// The styled spans of the applied set, anchored to the styled document; null while detached or with no applied tokens.
	private TextSegmentCollection<StyledToken>? _spans;

	/// <param name="textView">The text view that will be redrawn when semantic styles change.</param>
	/// <param name="styleResolver">The resolver that maps tokens to visual styles.</param>
	/// <exception cref="ArgumentNullException"><paramref name="textView"/> or <paramref name="styleResolver"/> is <see langword="null"/>.</exception>
	public SemanticTokensColorizer(TextView textView, ISemanticTokenStyleResolver styleResolver)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(styleResolver);

		_textView = textView;
		_styleResolver = styleResolver;
	}

	/// <summary>The number of derived typefaces the colorizer holds for its bold and italic styles.</summary>
	internal int DerivedTypefaceCount => _typefaceCache.Count;

	/// <summary>Replaces the semantic tokens currently applied to the text view.</summary>
	/// <param name="tokens">The semantic tokens to render; an empty collection clears the current tokens.</param>
	/// <returns>
	/// <see langword="true"/> when the push was applied (or cleared the applied tokens); <see langword="false"/> when the call was a no-op
	/// because the applied set is still current, or because an empty collection cleared nothing.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	/// <remarks>
	/// <para>
	/// The token collection is copied, so mutating the caller's list afterwards does not affect the colorizer. The token offsets
	/// are read against the document as it is when the call runs, so a set computed for older text paints at its offsets in the current text
	/// until the next set.
	/// </para>
	/// <para>
	/// A push is ignored when the document instance and its version are unchanged since the applied set and the pushed
	/// set holds the same ranges, types, and modifiers in the same order: the styled spans are kept, the resolver is not invoked again, and the view
	/// is not redrawn. After an edit, an equal set is applied again, because its offsets now describe the edited text.
	/// Call <see cref="Rebuild"/> when only the style resolver's configuration changed.
	/// </para>
	/// </remarks>
	public bool ApplyTokens(IReadOnlyList<SemanticToken> tokens)
	{
		ArgumentNullException.ThrowIfNull(tokens);
		_textView.Dispatcher.VerifyAccess();

		if (tokens.Count == 0)
			return ClearTokens();

		if (IsRepeatPush(tokens))
			return false;

		// The new spans are built completely before they are committed: a throwing resolver must leave the
		// set applied above (including the repeat-push identity) untouched, so the same push can be
		// retried once the resolver configuration is fixed.
		SemanticToken[] captured = [.. tokens];
		TextDocument? document = _textView.Document;
		List<StyledToken>? spans = document is null ? null : Anchor(captured, document);

		Commit(captured, document, spans);
		_textView.Redraw();

		return true;
	}

	/// <summary>Resolves the styles of the most recent token set again.</summary>
	/// <returns>
	/// <see langword="true"/> when the styles were resolved and the view was redrawn; <see langword="false"/> when no tokens have been applied,
	/// the view is detached, or the view shows a document other than the styled one.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	/// <remarks>
	/// <para>
	/// Call this after the style resolver's configuration changes so the styles reflect the new configuration. The spans keep the positions
	/// they reached through the edits since the set was applied. A document that replaced the styled one has no spans to restyle, so the call paints
	/// nothing and the next push styles that document.
	/// </para>
	/// <para>
	/// Rebuilding always re-resolves every applied token, even when the token set did not change, so it is not subject to the repeat-push shortcut
	/// of <see cref="ApplyTokens"/>.
	/// </para>
	/// </remarks>
	public bool Rebuild()
	{
		_textView.Dispatcher.VerifyAccess();

		TextDocument? document = _textView.Document;

		if (_tokens is null || _spans is null || document is null || !ReferenceEquals(_styledDocument, document))
			return false;

		// The styles are resolved before any is assigned, so a throwing resolver keeps the previous styling in effect.
		var styles = new Action<VisualLineElement>?[_spans.Count];
		int index = 0;

		foreach (StyledToken span in _spans)
			styles[index++] = ResolveStyle(_tokens[span.TokenIndex]);

		index = 0;

		foreach (StyledToken span in _spans)
			span.Style = styles[index++];

		_textView.Redraw();

		return true;
	}

	/// <summary>Removes all semantic token styling from the text view.</summary>
	/// <returns>
	/// <see langword="true"/> when applied tokens were removed and the view was redrawn; <see langword="false"/> when no tokens have been applied.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	public bool ClearTokens()
	{
		_textView.Dispatcher.VerifyAccess();

		if (_tokens is null)
			return false;

		_tokens = null;
		_styledDocument = null;
		_styledVersion = null;
		_spans = null;
		_textView.Redraw();

		return true;
	}

	/// <inheritdoc/>
	protected override void ColorizeLine(DocumentLine line)
	{
		// The common unstyled case must not pay a span lookup per rendered line.
		if (_spans is null || _spans.Count == 0 || !ReferenceEquals(CurrentContext.Document, _styledDocument))
			return;

		int lineEndOffset = line.EndOffset;

		_lineTokens.Clear();

		// A span that starts on an earlier line belongs to that line, so only the spans that start on this one are drawn here.
		for (
			StyledToken? span = _spans.FindFirstSegmentWithStartAfter(line.Offset);
			span is not null && span.StartOffset < lineEndOffset;
			span = _spans.GetNextSegment(span)
		)
		{
			if (span.Style is not null && span.Length > 0)
				_lineTokens.Add(span);
		}

		if (_lineTokens.Count == 0)
			return;

		if (_lineTokens.Count > 1)
			_lineTokens.Sort(s_paintOrder);

		foreach (StyledToken span in _lineTokens)
		{
			if (span.Style is { } style)
				ChangeLinePart(span.StartOffset, Math.Min(span.EndOffset, lineEndOffset), style);
		}

		_lineTokens.Clear();
	}

	private static int ComparePaintOrder(StyledToken left, StyledToken right)
	{
		int byStart = left.StartOffset.CompareTo(right.StartOffset);

		if (byStart != 0)
			return byStart;

		int byLength = left.Length.CompareTo(right.Length);

		return byLength != 0 ? byLength : left.TokenIndex.CompareTo(right.TokenIndex);
	}

	private static bool IsSameVersion(ITextSourceVersion? applied, ITextSourceVersion? current) =>
		applied is not null && current is not null && applied.BelongsToSameDocumentAs(current) && applied.CompareAge(current) == 0;

	/// <summary>
	/// Determines whether the pushed token set is already applied to the current document as it is now, so applying it again would not change
	/// the styled spans or the rendered output.
	/// </summary>
	/// <remarks>
	/// The document instance and its version must match the applied set: an edit moves the anchored spans away from the token offsets, so a set
	/// pushed after it describes the edited text even when its ranges are numerically equal.
	/// </remarks>
	private bool IsRepeatPush(IReadOnlyList<SemanticToken> tokens)
	{
		TextDocument? document = _textView.Document;

		if (_tokens is null || !ReferenceEquals(_styledDocument, document))
			return false;

		// While the view is detached nothing is anchored, so the token sequence alone decides the repeat.
		if (document is not null && !IsSameVersion(_styledVersion, document.Version))
			return false;

		if (_tokens.Length != tokens.Count)
			return false;

		for (int index = 0; index < _tokens.Length; index++)
		{
			if (!_tokens[index].Equals(tokens[index]))
				return false;
		}

		return true;
	}

	// Places every token on the line that contains its start offset, clipped to that line. A token outside the text, or one that covers no
	// character of its start line, has nothing to draw and no span; a token whose style resolves to null keeps its span, so a rebuild can style it.
	private List<StyledToken> Anchor(SemanticToken[] tokens, TextDocument document)
	{
		int textLength = document.TextLength;
		List<StyledToken> spans = new(tokens.Length);

		for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
		{
			SemanticToken token = tokens[tokenIndex];

			if (token.Range.Offset >= textLength)
				continue;

			DocumentLine line = document.GetLineByOffset(token.Range.Offset);
			int endOffset = Math.Min(token.Range.EndOffset, line.EndOffset);

			if (endOffset <= token.Range.Offset)
				continue;

			spans.Add(new StyledToken(tokenIndex, ResolveStyle(token)) { StartOffset = token.Range.Offset, EndOffset = endOffset });
		}

		return spans;
	}

	private void Commit(SemanticToken[] tokens, TextDocument? document, List<StyledToken>? spans)
	{
		TextSegmentCollection<StyledToken>? anchored = null;

		if (document is not null && spans is not null)
		{
			// The collection of the styled document is reused; a new document needs a collection that follows its edits.
			anchored = _spans is not null && ReferenceEquals(_styledDocument, document) ? _spans : new TextSegmentCollection<StyledToken>(document);
			anchored.Clear();

			foreach (StyledToken span in spans)
				anchored.Add(span);
		}

		_tokens = tokens;
		_styledDocument = document;
		_styledVersion = document?.Version;
		_spans = anchored;
	}

	private Action<VisualLineElement>? ResolveStyle(SemanticToken token)
	{
		TextRunStyle style = _styleResolver.Resolve(token);

		if (!style.HasFormatting)
			return null;

		// The apply delegate is created once per styled token so the paint path allocates nothing per pass. A bold or italic style also
		// takes its typeface from the cache, which derives it once per base typeface instead of once per element and pass.
		if (style.IsBold || style.IsItalic)
			return element => TextRunStyleApplier.Apply(element, style, _typefaceCache.GetOrAdd(style, element.TextRunProperties.Typeface));

		return element => TextRunStyleApplier.Apply(element, style);
	}

	/// <summary>The styled span of one applied token, whose offsets the collection moves with the edits of its document.</summary>
	private sealed class StyledToken(int tokenIndex, Action<VisualLineElement>? style) : TextSegment
	{
		/// <summary>The position of the token in the applied set.</summary>
		internal int TokenIndex { get; } = tokenIndex;

		/// <summary>The style the span is painted with, or <see langword="null"/> when its token carries no formatting.</summary>
		internal Action<VisualLineElement>? Style { get; set; } = style;
	}
}
