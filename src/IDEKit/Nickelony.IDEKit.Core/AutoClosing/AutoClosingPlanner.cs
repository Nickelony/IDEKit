using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Resolves the auto-closing action for text entered at a caret position.</summary>
/// <remarks>
/// The editor-neutral, stateless core of auto-closing: an editor binding composes it with its own input handling, read-only policy, document edits,
/// and insertion tracking. Resolution reads only <see cref="ITextCharSource.TextLength"/> and <see cref="ITextCharSource.GetCharAt(int)"/> and finds
/// the caret's line end with a terminator scan, so a snapshot with lazy line storage stays lazy on the typing path.
/// </remarks>
public static class AutoClosingPlanner
{
	// The built-in AutoCloseBefore presets as SearchValues, so the gate is a constant-time membership
	// test instead of a scan of the preset string. A host-configured AutoCloseBefore stays a string:
	// a per-options SearchValues would have to be an instance field and would then take part in the
	// options record's value equality, and string.Contains(char) already scans it with vectorization.
	private static readonly SearchValues<char> s_defaultBracketAutoCloseBefore = SearchValues.Create(
		AutoClosingOptions.DefaultBracketAutoCloseBefore
	);

	private static readonly SearchValues<char> s_defaultQuoteAutoCloseBefore = SearchValues.Create(AutoClosingOptions.DefaultQuoteAutoCloseBefore);

	/// <summary>Tries to resolve an auto-closing action for <see cref="AutoClosingRequest.InputText"/> at the request's caret offset.</summary>
	/// <returns><see langword="true"/> when an action applies; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <see cref="AutoClosingRequest.Snapshot"/>, <see cref="AutoClosingRequest.InputText"/>,
	/// or <see cref="AutoClosingRequest.Options"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Closing text matches exactly, or by its leading token when it spans multiple characters, so typing <c>}</c> skips an existing <c>},</c>;
	/// input that matches neither resolves no action. See <see cref="AutoClosingOptions"/> and <see cref="AutoClosingPair"/> for the gates
	/// that decide whether a matching pair inserts, skips, or wraps.
	/// </remarks>
	public static bool TryPlanAction(in AutoClosingRequest request, out AutoClosingAction action)
	{
		ArgumentNullException.ThrowIfNull(request.Snapshot);
		ArgumentNullException.ThrowIfNull(request.InputText);
		ArgumentNullException.ThrowIfNull(request.Options);

		action = default;

		if (request.InputText.Length == 0)
			return false;

		// Offsets outside the snapshot are clamped before the pair gates inspect the surrounding text.
		AutoClosingRequest context = request with
		{
			CaretOffset = Math.Clamp(request.CaretOffset, 0, request.Snapshot.TextLength),
		};

		IReadOnlyList<AutoClosingPair> pairs = context.Options.Pairs;

		for (int index = 0; index < pairs.Count; index++)
		{
			if (TryResolvePairAction(pairs[index], in context, out action))
				return true;
		}

		return false;
	}

	/// <summary>Tries to resolve the auto-closing pair a host pair deletion removes.</summary>
	/// <returns>
	/// <see langword="true"/> when a pair applies; otherwise, <see langword="false"/> and <paramref name="pair"/> is <see langword="null"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <see cref="AutoClosingDeletionRequest.Snapshot"/> or <see cref="AutoClosingDeletionRequest.Options"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The pairs are evaluated in order and the first whose opening token ends at the caret and whose closing text starts there wins.
	/// A quote-like pair is not deleted when an odd run of escape characters precedes its opening token or its closing text. Whether the closing text
	/// qualifies follows <see cref="AutoClosingOptions.PairDeletionTracking"/>.
	/// </remarks>
	public static bool TryPlanPairDeletion(in AutoClosingDeletionRequest request, [NotNullWhen(true)] out AutoClosingPair? pair)
	{
		ITextSnapshot snapshot = request.Snapshot;
		AutoClosingOptions options = request.Options;

		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(options);

		pair = null;
		int caretOffset = Math.Clamp(request.CaretOffset, 0, snapshot.TextLength);

		IReadOnlyList<AutoClosingPair> pairs = options.Pairs;

		for (int index = 0; index < pairs.Count; index++)
		{
			AutoClosingPair candidate = pairs[index];

			string openingToken = candidate.Open;
			string closingString = candidate.Close;

			if (string.IsNullOrEmpty(openingToken) || string.IsNullOrEmpty(closingString))
				continue;

			if (
				!TextMatchProbe.MatchesAt(snapshot, caretOffset - openingToken.Length, openingToken)
				|| !TextMatchProbe.MatchesAt(snapshot, caretOffset, closingString)
			)
			{
				continue;
			}

			// An escaped quote-like token is part of an escape sequence, so it is not a pair member;
			// both the opening token's position and the closing text at the caret are checked.
			if (
				candidate.Kind == AutoClosingPairKind.Quote
				&& (
					IsEscapedAt(snapshot, caretOffset - openingToken.Length, options.EscapeCharacter)
					|| IsEscapedAt(snapshot, caretOffset, options.EscapeCharacter)
				)
			)
			{
				continue;
			}

			if (!AppliesToExistingClosingText(options.PairDeletionTracking, caretOffset, request.IsTrackedClosingText))
				continue;

			pair = candidate;
			return true;
		}

		return false;
	}

	/// <summary>Resolves the action for one pair.</summary>
	/// <returns><see langword="true"/> when the pair produced an action; otherwise, <see langword="false"/>.</returns>
	private static bool TryResolvePairAction(AutoClosingPair pair, in AutoClosingRequest context, out AutoClosingAction action)
	{
		action = default;

		string openingToken = pair.Open;
		string closingString = pair.Close;

		// A pair without an opening or closing text cannot contribute an action.
		if (string.IsNullOrEmpty(openingToken) || string.IsNullOrEmpty(closingString))
			return false;

		if (MatchesOpeningToken(context, openingToken))
			return TryResolveOpeningToken(pair, in context, openingToken, closingString, out action);

		// Typing the closing text, or the leading token of a multi-character closing text, can skip the
		// configured closing text when it is already at the caret. Skip tracking decides whether a
		// manually written closing text is skipped as well. As in the opening-token path, an escaped
		// quote-like token belongs to an escape sequence and is not skipped, and a wrapping request
		// never resolves a skip: the typed token replaces the selection instead.
		if (
			!context.IsWrappingSelection
			&& IsClosingInput(context.InputText, closingString)
			&& (pair.Kind != AutoClosingPairKind.Quote || !IsEscaped(context))
			&& TextMatchProbe.MatchesAt(context.Snapshot, context.CaretOffset, closingString)
			&& AppliesToExistingClosingText(context.Options.ClosingTextSkipTracking, context.CaretOffset, context.IsTrackedClosingText)
		)
		{
			action = AutoClosingAction.CreateSkip(closingString);
			return true;
		}

		return false;
	}

	/// <summary>Resolves the action for an input that matches the pair's opening token.</summary>
	/// <returns><see langword="true"/> when the pair produced an action; otherwise, <see langword="false"/>.</returns>
	private static bool TryResolveOpeningToken(
		AutoClosingPair pair,
		in AutoClosingRequest context,
		string openingToken,
		string closingString,
		out AutoClosingAction action
	)
	{
		action = default;

		if (context.IsWrappingSelection)
		{
			// Typing over a selection replaces it unless the pair opts into wrapping it.
			if (!pair.WrapSelection)
				return false;

			action = AutoClosingAction.CreateInsert(closingString);
			return true;
		}

		bool isQuotePair = pair.Kind == AutoClosingPairKind.Quote;

		// A quote-like pair can be skipped as a whole: typing the opening token over its existing closing
		// text moves past that text instead of starting a new pair.
		if (
			isQuotePair
			&& !IsEscaped(context)
			&& TextMatchProbe.MatchesAt(context.Snapshot, context.CaretOffset, closingString)
			&& AppliesToExistingClosingText(context.Options.ClosingTextSkipTracking, context.CaretOffset, context.IsTrackedClosingText)
		)
		{
			action = AutoClosingAction.CreateSkip(closingString);
			return true;
		}

		// Let normal text input add the token when the same token immediately precedes the caret, so
		// token runs can be built. Quote-like pairs always use this rule; a bracket-like pair uses it only
		// when its opening token is also its closing text, because the opening token of an asymmetric
		// pair legitimately repeats (the inner '(' of '(())' must still insert its closing text).
		if (
			(isQuotePair || string.Equals(openingToken, closingString, StringComparison.Ordinal))
			&& TextMatchProbe.MatchesAt(context.Snapshot, context.CaretOffset - openingToken.Length, openingToken)
		)
		{
			return false;
		}

		if (isQuotePair)
		{
			// Quotes usually do not auto-close after a word character; backticks usually keep this disabled.
			if (!context.Options.AutoCloseUnconditionally && pair.SuppressAfterWordCharacter && IsWordCharacterBeforeCaret(context))
				return false;
		}
		// Typing an opening token in front of its closing text must not duplicate that text; typing '('
		// before ')' leaves a single pair. The scan reads raw text, so a pair can disable it when the
		// host already tokenizes (see AutoClosingPair.CheckUnmatchedClosingText).
		else if (
			pair.CheckUnmatchedClosingText
			&& !context.Options.AutoCloseUnconditionally
			&& HasUnmatchedClosingTextAfter(context, openingToken, closingString)
		)
		{
			return false;
		}

		// Auto-closing applies when the character after the caret is the end of the text, whitespace, or an
		// allowed character for the pair's kind, unless AutoCloseUnconditionally is set.
		if (!ShouldAutoCloseBefore(context, pair.Kind))
			return false;

		action = AutoClosingAction.CreateInsert(closingString);
		return true;
	}

	/// <summary>
	/// Determines whether the entered text completes the opening token at the caret: the input must end the token, and the token's remaining
	/// characters must match the text ending immediately before the caret, so a multi-character opener resolves when its final character is typed.
	/// </summary>
	private static bool MatchesOpeningToken(in AutoClosingRequest context, string openingToken)
	{
		string inputText = context.InputText;

		if (inputText.Length == 0 || inputText.Length > openingToken.Length)
			return false;

		if (!openingToken.EndsWith(inputText, StringComparison.Ordinal))
			return false;

		int precedingLength = openingToken.Length - inputText.Length;

		return precedingLength == 0
			|| TextMatchProbe.MatchesAt(context.Snapshot, context.CaretOffset - precedingLength, openingToken[..precedingLength]);
	}

	/// <summary>Determines whether the input matches the closing text or its leading token.</summary>
	private static bool IsClosingInput(string inputText, string closingString)
	{
		if (string.Equals(inputText, closingString, StringComparison.Ordinal))
			return true;

		// A multi-character closing text is also skipped by typing its leading token, matching how
		// single-character closings behave (for example typing '}' skips an existing '},').
		return closingString.Length > 1 && inputText.Length == 1 && inputText[0] == closingString[0];
	}

	/// <summary>
	/// Determines whether the token starting at <paramref name="tokenStart"/> is escaped by an odd run of the configured escape character directly
	/// before it.
	/// </summary>
	private static bool IsEscapedAt(ITextSnapshot snapshot, int tokenStart, char? escapeCharacter)
	{
		if (escapeCharacter is not char escape)
			return false;

		var probe = new EscapeRunProbe(escape);

		for (int position = tokenStart; position > 0 && probe.Consume(snapshot.GetCharAt(position - 1)); position--)
		{ }

		return probe.IsEscaped;
	}

	/// <summary>Determines whether the token at the caret is escaped, so it belongs to an escape sequence instead of an auto-closing token.</summary>
	private static bool IsEscaped(in AutoClosingRequest context) =>
		IsEscapedAt(context.Snapshot, context.CaretOffset, context.Options.EscapeCharacter);

	/// <summary>
	/// Determines whether the caret's line contains a closing text that no opening token after the caret matches, so inserting the closing text
	/// would duplicate an existing one.
	/// </summary>
	private static bool HasUnmatchedClosingTextAfter(in AutoClosingRequest context, string openingToken, string closingString)
	{
		int position = context.CaretOffset;
		int lineEndOffset = FindLineEndOffset(context.Snapshot, position);
		int depth = 0;

		while (position < lineEndOffset)
		{
			if (TextMatchProbe.MatchesAt(context.Snapshot, position, openingToken))
			{
				depth++;
				position += openingToken.Length;
			}
			else if (TextMatchProbe.MatchesAt(context.Snapshot, position, closingString))
			{
				if (depth == 0)
					return true;

				depth--;
				position += closingString.Length;
			}
			else
			{
				position++;
			}
		}

		return false;
	}

	/// <summary>Finds the end of the line containing <paramref name="offset"/>, exclusive of the line terminator.</summary>
	/// <returns>The zero-based offset at which the line's terminator starts, or the text end.</returns>
	/// <remarks>
	/// A position on a line terminator belongs to the preceding line; a position on the LF of a CRLF pair is normalized to the CR first, so the scan
	/// reports the CR as the terminator start without walking the line content.
	/// </remarks>
	private static int FindLineEndOffset(ITextSnapshot snapshot, int offset)
	{
		if (offset > 0 && offset < snapshot.TextLength && LineTerminators.IsCrLfPair(snapshot.GetCharAt(offset - 1), snapshot.GetCharAt(offset)))
			offset--;

		while (offset < snapshot.TextLength && !LineTerminators.IsTerminator(snapshot.GetCharAt(offset)))
			offset++;

		return offset;
	}

	/// <summary>
	/// Determines whether auto-closing applies for the character after the caret: the end of the text, whitespace, or a character the pair's kind
	/// allows at that position.
	/// </summary>
	private static bool ShouldAutoCloseBefore(in AutoClosingRequest context, AutoClosingPairKind kind)
	{
		if (context.Options.AutoCloseUnconditionally || context.CaretOffset >= context.Snapshot.TextLength)
			return true;

		char nextCharacter = context.Snapshot.GetCharAt(context.CaretOffset);

		if (char.IsWhiteSpace(nextCharacter))
			return true;

		// A configured set replaces the kind's default set; an empty set therefore admits no character
		// besides whitespace and the end of the text.
		if (context.Options.AutoCloseBefore is { } autoCloseBefore)
			return autoCloseBefore.Contains(nextCharacter);

		return (kind == AutoClosingPairKind.Quote ? s_defaultQuoteAutoCloseBefore : s_defaultBracketAutoCloseBefore).Contains(nextCharacter);
	}

	/// <summary>
	/// Determines whether the character immediately before the caret counts as a word character under the configured identifier policy.
	/// </summary>
	private static bool IsWordCharacterBeforeCaret(in AutoClosingRequest context) =>
		context.CaretOffset > 0 && context.Options.IdentifierPolicy.IsPartCharacter(context.Snapshot.GetCharAt(context.CaretOffset - 1));

	/// <summary>Applies a tracking mode to the closing text at the caret.</summary>
	private static bool AppliesToExistingClosingText(AutoClosingTrackingMode mode, int offset, Func<int, bool>? isTrackedClosingText)
	{
		return mode switch
		{
			AutoClosingTrackingMode.Always => true,
			AutoClosingTrackingMode.Never => false,
			_ => isTrackedClosingText is { } isTracked && isTracked(offset),
		};
	}
}
