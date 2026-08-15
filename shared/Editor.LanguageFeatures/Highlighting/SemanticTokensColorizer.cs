#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.AvalonEdit.Rendering;
#endif
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Collections.Frozen;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting;
#endif

/// <summary>
/// Applies resolved semantic-token styles while the editor renders the text view. Each token is
/// assigned to the line containing its start offset, clamped to the current document when the styled
/// map is built, and clipped to the rendered line during rendering.
/// </summary>
/// <remarks>
/// <para>
/// The host adds the colorizer to the text view's line transformers and must remove it again before the view
/// it was registered for is torn down (for example when the editor is closed or the view is replaced); the
/// colorizer itself registers no event handlers. <see cref="SetTokens"/>, <see cref="Rebuild"/>, and
/// <see cref="ClearTokens"/> redraw the text view and must be called on the UI thread; each verifies
/// the caller's access and throws <see cref="InvalidOperationException"/> off that thread.
/// </para>
/// <para>
/// A push, rebuild, or clear redraws the whole text view rather than an offset range. The colorizer is a
/// line transformer, so a new token set can restyle any line and the editor exposes no per-line invalidation
/// for one; a scoped redraw would leave the lines outside the range showing the previous styling. The
/// TextMate transformer can scope its redraws because it is handed the changed range, which a token push does
/// not carry.
/// </para>
/// <para>
/// Styled positions are captured when the token set is applied: each token is converted to a zero-based line
/// number, character index, and length from the document as it exists at that moment. After document edits the
/// stored styles stay anchored to those line and character positions (clamped to the rendered line), so they no
/// longer track the text they were computed for. The colorizer cannot realign them on its own; the host is
/// responsible for pushing a fresh token set after edits.
/// </para>
/// <para>
/// The style resolver is invoked synchronously while the styled map is built; a resolver that throws aborts the
/// current token application with the exception escaping to the caller. The previously applied token set and its
/// styling remain in effect, so the push can be retried after the resolver configuration is fixed.
/// </para>
/// </remarks>
public sealed class SemanticTokensColorizer : DocumentColorizingTransformer
{
	private static readonly IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> s_emptyTokensByLine =
		FrozenDictionary<int, IReadOnlyList<StyledSemanticToken>>.Empty;

	private readonly TextView _textView;
	private readonly ISemanticTokenStyleResolver _styleResolver;

	private TextSemanticToken[] _rawTokens = [];

	// The document instance the styled map was built from. Together with the token sequence it forms the
	// identity the repeat-push check compares against; null is a valid value (applied while detached).
	private TextDocument? _styledDocument;

	private IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> _tokensByLine = s_emptyTokensByLine;

	// The line, character, and length derived for every applied token, parallel to _rawTokens. The repeat-push
	// check compares the anchors derived from the current document against these values, because an edit can
	// move an anchor without changing the token ranges.
	private (int Line, int Character, int Length)[] _appliedAnchors = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="SemanticTokensColorizer"/> class.
	/// </summary>
	/// <param name="textView">The text view that will be redrawn when semantic styles change.</param>
	/// <param name="styleResolver">The resolver that maps tokens to visual styles.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/> or <paramref name="styleResolver"/> is <see langword="null"/>.
	/// </exception>
	public SemanticTokensColorizer(TextView textView, ISemanticTokenStyleResolver styleResolver)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(styleResolver);

		_textView = textView;
		_styleResolver = styleResolver;
	}

	/// <summary>
	/// Replaces the semantic tokens currently applied to the text view.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The token collection is copied, so mutating the caller's list afterwards does not affect the colorizer.
	/// </para>
	/// <para>
	/// Each token is assigned to the line containing its start offset and clipped to that line, so tokens spanning
	/// multiple lines are rendered only on their start line.
	/// </para>
	/// <para>
	/// Call this again after document edits to keep tokens aligned, or call <see cref="Rebuild"/> when only the
	/// style resolver's configuration changed. A push is ignored when the document instance is unchanged and the
	/// pushed set still maps to the same line and character anchors (same ranges, types, and modifiers in the same
	/// order): the styled map is kept, the resolver is not invoked again, and the view is not redrawn. A throwing
	/// resolver leaves the previously applied set in effect, so the push can be retried.
	/// </para>
	/// </remarks>
	/// <param name="tokens">The semantic tokens to render; an empty collection clears the current tokens.</param>
	/// <returns>
	/// <see langword="true"/> when the push was applied (or cleared the applied tokens); <see langword="false"/>
	/// when the call was a no-op because the applied set is still current, or because an empty collection cleared
	/// nothing.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	public bool SetTokens(IReadOnlyList<TextSemanticToken> tokens)
	{
		ArgumentNullException.ThrowIfNull(tokens);
		_textView.Dispatcher.VerifyAccess();

		if (tokens.Count == 0)
			return ClearTokens();

		if (IsRepeatPush(tokens))
			return false;

		// The new state is built completely before it is committed: a throwing resolver must leave the
		// previously applied set (including the repeat-push identity) untouched, so the same push can be
		// retried once the resolver configuration is fixed.
		TextSemanticToken[] appliedTokens = [.. tokens];
		(IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> styledMap, (int Line, int Character, int Length)[] anchors) =
			BuildStyledState(appliedTokens);

		_rawTokens = appliedTokens;
		_styledDocument = _textView.Document;
		_tokensByLine = styledMap;
		_appliedAnchors = anchors;
		_textView.Redraw();

		return true;
	}

	/// <summary>
	/// Rebuilds the styled semantic token cache from the most recent token set. Call this after the
	/// style resolver's configuration changes so the cached styles reflect the new configuration.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tokens whose start offset lies outside the document text (including an offset at the document end) are
	/// ignored, and tokens extending past their start line are clipped to that line. While the view is
	/// detached, the rebuilt map is empty; the applied token set stays stored and is rendered again after the
	/// next rebuild or push once a document is attached.
	/// </para>
	/// <para>
	/// Rebuilding always re-resolves every applied token, even when the token set did not change, so it is not
	/// subject to the repeat-push shortcut of <see cref="SetTokens"/>.
	/// </para>
	/// </remarks>
	/// <returns>
	/// <see langword="true"/> when the styled map was rebuilt and the view was redrawn; <see langword="false"/>
	/// when no tokens have been applied.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	public bool Rebuild()
	{
		_textView.Dispatcher.VerifyAccess();

		if (_rawTokens.Length == 0)
			return false;

		// Rebuild from the applied set; the state is only committed when the whole map was built, so a
		// throwing resolver keeps the previous styling and anchors in effect.
		(IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> styledMap, (int Line, int Character, int Length)[] anchors) =
			BuildStyledState(_rawTokens);

		_styledDocument = _textView.Document;
		_tokensByLine = styledMap;
		_appliedAnchors = anchors;
		_textView.Redraw();

		return true;
	}

	/// <summary>
	/// Removes all semantic token styling from the text view.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when applied tokens were removed and the view was redrawn; <see langword="false"/>
	/// when no tokens have been applied.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	public bool ClearTokens()
	{
		_textView.Dispatcher.VerifyAccess();

		if (_rawTokens.Length == 0)
			return false;

		_rawTokens = [];
		_styledDocument = null;
		_tokensByLine = s_emptyTokensByLine;
		_appliedAnchors = [];
		_textView.Redraw();

		return true;
	}

	/// <inheritdoc/>
	protected override void ColorizeLine(DocumentLine line)
	{
		// The common unstyled case must not pay a dictionary lookup per rendered line.
		if (_tokensByLine.Count == 0)
			return;

		if (!_tokensByLine.TryGetValue(line.LineNumber - 1, out IReadOnlyList<StyledSemanticToken>? tokens))
			return;

		int lineLength = line.Length;

		for (int i = 0; i < tokens.Count; i++)
		{
			StyledSemanticToken styled = tokens[i];
			int startIndex = Math.Max(0, Math.Min(styled.Character, lineLength));

			// The end index is clamped with a subtraction instead of summing the token's character and
			// length first: an overflowing sum would come out negative and silently hide the token.
			int endIndex = styled.Length >= lineLength - startIndex
				? lineLength
				: startIndex + styled.Length;

			if (endIndex <= startIndex)
				continue;

			ChangeLinePart(line.Offset + startIndex, line.Offset + endIndex, styled.Apply);
		}
	}

	/// <summary>
	/// Determines whether the pushed token set is already applied for the current document, so applying it
	/// again would not change the styled map or the rendered output.
	/// </summary>
	/// <remarks>
	/// The document instance must match, and the anchors derived from the current document must equal the
	/// applied anchors for every token. A replaced document, or an edit that moved a token's line or character
	/// without changing its offsets (for example a same-length replacement that changed the line structure),
	/// invalidates the anchors even when the token ranges are numerically equal, so the push is applied again.
	/// </remarks>
	private bool IsRepeatPush(IReadOnlyList<TextSemanticToken> tokens)
	{
		if (!ReferenceEquals(_styledDocument, _textView.Document))
			return false;

		if (!AreTokenSequencesEqual(_rawTokens, tokens))
			return false;

		TextDocument? document = _textView.Document;

		// Both the applied anchors and the derived anchors are invalid while the view is detached, so the
		// token sequence alone decides the repeat.
		if (document is null)
			return true;

		for (int i = 0; i < tokens.Count; i++)
		{
			if (_appliedAnchors[i] != ToLineColumn(tokens[i], document))
				return false;
		}

		return true;
	}

	private static bool AreTokenSequencesEqual(
		TextSemanticToken[] appliedTokens,
		IReadOnlyList<TextSemanticToken> pushedTokens)
	{
		if (appliedTokens.Length != pushedTokens.Count)
			return false;

		// TextSemanticToken implements structural value equality over range, type, and modifier sequence.
		for (int i = 0; i < appliedTokens.Length; i++)
		{
			if (!appliedTokens[i].Equals(pushedTokens[i]))
				return false;
		}

		return true;
	}

	private (IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> TokensByLine, (int Line, int Character, int Length)[] Anchors) BuildStyledState(
		TextSemanticToken[] tokens)
	{
		var anchors = new (int Line, int Character, int Length)[tokens.Length];
		TextDocument? document = _textView.Document;

		if (tokens.Length == 0 || document is null)
			return (s_emptyTokensByLine, anchors);

		var grouped = new Dictionary<int, List<StyledSemanticToken>>();

		for (int i = 0; i < tokens.Length; i++)
		{
			TextSemanticToken token = tokens[i];
			(int line, int character, int length) = ToLineColumn(token, document);
			anchors[i] = (line, character, length);

			if (line < 0 || length <= 0)
				continue;

			TextRunStyle style = _styleResolver.Resolve(token);

			if (!style.HasFormatting)
				continue;

			if (!grouped.TryGetValue(line, out List<StyledSemanticToken>? lineTokens))
			{
				lineTokens = [];
				grouped[line] = lineTokens;
			}

			// The apply delegate is cached per styled token so the paint path allocates nothing per pass.
			lineTokens.Add(new StyledSemanticToken(character, length, element => TextRunStyleApplier.Apply(element, style)));
		}

		var tokensByLine = new Dictionary<int, IReadOnlyList<StyledSemanticToken>>(grouped.Count);

		foreach (KeyValuePair<int, List<StyledSemanticToken>> pair in grouped)
		{
			pair.Value.Sort(
				static (left, right) =>
				{
					int characterComparison = left.Character.CompareTo(right.Character);
					return characterComparison != 0 ? characterComparison : left.Length.CompareTo(right.Length);
				});

			tokensByLine[pair.Key] = pair.Value;
		}

		return (tokensByLine, anchors);
	}

	private static (int Line, int Character, int Length) ToLineColumn(TextSemanticToken token, TextDocument document)
	{
		int startOffset = document.ClampOffset(token.Range.Offset);
		int endOffset = Math.Max(startOffset, document.ClampOffset(token.Range.EndOffset));

		if (startOffset >= document.TextLength)
			return (-1, 0, 0);

		DocumentLine line = document.GetLineByOffset(startOffset);
		int character = startOffset - line.Offset;
		int lineEnd = Math.Min(line.EndOffset, document.TextLength);
		int length = Math.Max(0, Math.Min(endOffset, lineEnd) - startOffset);

		return (line.LineNumber - 1, character, length);
	}

	private readonly struct StyledSemanticToken(int character, int length, Action<VisualLineElement> apply)
	{
		public int Character { get; } = character;
		public int Length { get; } = length;
		public Action<VisualLineElement> Apply { get; } = apply;
	}
}
