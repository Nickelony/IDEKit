using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

/// <summary>Expands LSP snippet syntax into visible text and a tabstop model that hosts can use for snippet sessions.</summary>
/// <remarks>
/// The expander understands the subset of the LSP snippet grammar that completion providers actually emit; a construct outside that subset
/// keeps its literal text and no input is ever dropped or truncated. Any escape character other than the supported ones keeps its backslash,
/// because the expander is a tolerant consumer of real-world snippet text rather than a validating parser. Expansion never throws for text input.
/// </remarks>
public static class SnippetExpander
{
	/// <summary>The maximum nesting depth of default-text placeholders the expander resolves; a deeper one stays literal.</summary>
	private const int MaxNestingDepth = 32;

	/// <summary>Expands <paramref name="snippet"/> into its visible text and placeholder model.</summary>
	/// <param name="snippet">The snippet text to expand.</param>
	/// <returns>
	/// The expanded text together with the placeholders; plain text without <c>$</c> or <c>\</c> is returned unchanged with no placeholders.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="snippet"/> is <see langword="null"/>.</exception>
	public static SnippetExpansion Expand(string snippet)
	{
		ArgumentNullException.ThrowIfNull(snippet);

		if (snippet.Length == 0 || (snippet.IndexOf('$') < 0 && snippet.IndexOf('\\') < 0))
			return new(snippet, []);

		var builder = new StringBuilder(snippet.Length);
		var placeholders = new List<SnippetPlaceholder>();

		AppendExpanded(builder, placeholders, snippet, startIndex: 0, endIndex: snippet.Length, depth: 0);

		if (placeholders.Count > 1)
		{
			placeholders.Sort(
				static (left, right) =>
				{
					// Index 0 is the final caret stop, so it sorts after every numbered tabstop.
					if (left.Index == 0)
						return right.Index == 0 ? left.Range.Offset.CompareTo(right.Range.Offset) : 1;

					if (right.Index == 0)
						return -1;

					int byIndex = left.Index.CompareTo(right.Index);

					if (byIndex != 0)
						return byIndex;

					// Equal indexes: order by position, then by span length, so placeholders that start at
					// the same offset have one deterministic order even though List.Sort is unstable.
					int byOffset = left.Range.Offset.CompareTo(right.Range.Offset);

					return byOffset != 0 ? byOffset : left.Range.Length.CompareTo(right.Range.Length);
				}
			);
		}

		return new(builder.ToString(), placeholders);
	}

	/// <summary>Appends the visible text of one snippet range and records every placeholder it contains.</summary>
	/// <param name="builder">The builder receiving the visible text.</param>
	/// <param name="placeholders">The list receiving the placeholders.</param>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="startIndex">The inclusive start index of the range to process.</param>
	/// <param name="endIndex">The exclusive end index of the range to process.</param>
	/// <param name="depth">The nesting depth the range was reached at.</param>
	private static void AppendExpanded(
		StringBuilder builder,
		List<SnippetPlaceholder> placeholders,
		string snippet,
		int startIndex,
		int endIndex,
		int depth
	)
	{
		int index = startIndex;

		while (index < endIndex)
		{
			char character = snippet[index];

			// Snippet escapes: \$ becomes $, \} becomes }, and \\ becomes \. Any other escape
			// keeps its backslash because the expander does not validate the input.
			if (TryConsumeEscape(snippet, ref index, endIndex, choiceEscapes: false, target: builder))
				continue;

			if (character != '$')
			{
				builder.Append(character);
				index++;
				continue;
			}

			if (SnippetSyntaxProbe.IsBraceOpenAt(snippet, index, endIndex))
			{
				int contentStartIndex = index + 2;

				// A digit run followed by '|' starts a choice placeholder; its text runs to the
				// terminating "|}" and may contain '}' as plain text, so it is scanned separately
				// from the brace-depth path.
				if (
					SnippetSyntaxProbe.IsChoiceHeadAt(snippet, contentStartIndex, endIndex, out int digitEndIndex)
					&& SnippetSyntaxProbe.TryParseIndex(snippet, startIndex: contentStartIndex, endIndex: digitEndIndex, value: out int choiceIndex)
					&& choiceIndex > 0
				)
				{
					if (
						TryExpandChoice(
							builder,
							placeholders,
							snippet,
							tabstopIndex: choiceIndex,
							bodyStartIndex: digitEndIndex + 1,
							endIndex,
							out int choiceEndIndex
						)
					)
					{
						index = choiceEndIndex;
						continue;
					}

					// A malformed choice (missing |} terminator or an empty element) stays literal;
					// the '$' is emitted and scanning resumes inside the construct so later escapes and
					// constructs still resolve.
					builder.Append(character);
					index++;
					continue;
				}

				int closeIndex = FindConstructEnd(snippet, contentStartIndex, endIndex, out bool hasCloseBrace);

				if (closeIndex < 0)
				{
					// A ${ without a matching close brace stays literal; the remaining characters
					// are reprocessed individually so nested escapes still resolve. When the suffix
					// contains no close brace at all, no construct in it can close either, so the
					// remainder is finished in one linear pass instead of rescanning per construct.
					builder.Append(character);

					if (!hasCloseBrace)
					{
						AppendUnclosable(builder, placeholders, snippet, startIndex: index + 1, endIndex);
						return;
					}

					index++;
					continue;
				}

				if (TryExpandBracedConstruct(builder, placeholders, snippet, constructStartIndex: index, closeIndex, depth))
				{
					index = closeIndex + 1;
					continue;
				}

				// An unsupported or malformed braced body keeps its literal text; the '$' is
				// emitted and scanning resumes inside the construct so nested escapes and
				// constructs still resolve, mirroring the malformed-choice path.
				builder.Append(character);
				index++;
				continue;
			}

			if (
				SnippetSyntaxProbe.TryReadIndex(
					snippet,
					startIndex: index + 1,
					endIndex,
					digitEndIndex: out int digitEnd,
					value: out int tabstopIndex
				)
			)
			{
				AddTabstop(builder, placeholders, tabstopIndex);
				index = digitEnd;
				continue;
			}

			// A lone $ or an unrepresentable tabstop index stays literal.
			builder.Append(character);
			index++;
		}
	}

	/// <summary>
	/// Appends a snippet range that contains no close brace, where no construct can close: every braced construct keeps its literal text
	/// while unbraced tabstops and escapes still resolve, matching the expansion path while avoiding repeated end-of-range rescans.
	/// </summary>
	/// <param name="builder">The builder receiving the visible text.</param>
	/// <param name="placeholders">The list receiving the placeholders.</param>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="startIndex">The inclusive start index of the range to process.</param>
	/// <param name="endIndex">The exclusive end index of the range to process.</param>
	private static void AppendUnclosable(StringBuilder builder, List<SnippetPlaceholder> placeholders, string snippet, int startIndex, int endIndex)
	{
		int index = startIndex;

		while (index < endIndex)
		{
			char character = snippet[index];

			if (TryConsumeEscape(snippet, ref index, endIndex, choiceEscapes: false, target: builder))
				continue;

			if (character == '$')
			{
				// Every braced construct is unterminated here, so the '$' stays literal and
				// scanning continues at the following character.
				if (SnippetSyntaxProbe.IsBraceOpenAt(snippet, index, endIndex))
				{
					builder.Append(character);
					index++;
					continue;
				}

				if (
					SnippetSyntaxProbe.TryReadIndex(
						snippet,
						startIndex: index + 1,
						endIndex,
						digitEndIndex: out int digitEnd,
						value: out int tabstopIndex
					)
				)
				{
					AddTabstop(builder, placeholders, tabstopIndex);
					index = digitEnd;
					continue;
				}
			}

			builder.Append(character);
			index++;
		}
	}

	/// <summary>
	/// Consumes one escape sequence at <paramref name="index"/> when the character is a backslash: the shared snippet
	/// escapes (<c>\$</c>, <c>\}</c>, <c>\\</c>) plus, when <paramref name="choiceEscapes"/> is set, the choice-text escapes <c>\,</c> and <c>\|</c>.
	/// Any other escape keeps its backslash and consumes only the backslash, because the expander does not validate the input.
	/// </summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="index">The current scan index; advanced past the consumed characters.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <param name="choiceEscapes">Whether <c>\,</c> and <c>\|</c> are escapes.</param>
	/// <param name="target">The builder receiving the resolved character.</param>
	/// <returns>
	/// <see langword="true"/> when the character was a backslash and the scan index was consumed; otherwise, <see langword="false"/>.
	/// </returns>
	private static bool TryConsumeEscape(string snippet, ref int index, int endIndex, bool choiceEscapes, StringBuilder target)
	{
		if (!SnippetSyntaxProbe.IsEscapeAt(snippet, index, endIndex))
			return false;

		char escapedCharacter = snippet[index + 1];

		if (escapedCharacter is '$' or '}' or '\\' || (choiceEscapes && escapedCharacter is ',' or '|'))
		{
			target.Append(escapedCharacter);
			index += 2;
			return true;
		}

		target.Append('\\');
		index++;
		return true;
	}

	/// <summary>Expands one tabstop or default-text construct (<c>${n}</c> or <c>${n:default}</c>) whose close brace is already known.</summary>
	/// <param name="builder">The builder receiving the visible text.</param>
	/// <param name="placeholders">The list receiving the placeholders.</param>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="constructStartIndex">The index of the construct's <c>$</c> character.</param>
	/// <param name="closeIndex">The index of the construct's close brace.</param>
	/// <param name="depth">The nesting depth of the enclosing range.</param>
	/// <returns>
	/// <see langword="true"/> when the construct was expanded; <see langword="false"/> when the body is outside the supported subset
	/// (or too deeply nested) and must stay literal.
	/// </returns>
	private static bool TryExpandBracedConstruct(
		StringBuilder builder,
		List<SnippetPlaceholder> placeholders,
		string snippet,
		int constructStartIndex,
		int closeIndex,
		int depth
	)
	{
		int contentStartIndex = constructStartIndex + 2;

		if (
			!SnippetSyntaxProbe.TryReadIndex(
				snippet,
				startIndex: contentStartIndex,
				endIndex: closeIndex,
				out int digitEndIndex,
				value: out int tabstopIndex
			)
		)
		{
			return false;
		}

		if (digitEndIndex == closeIndex)
		{
			// ${n} - a plain tabstop.
			AddTabstop(builder, placeholders, tabstopIndex);
			return true;
		}

		if (snippet[digitEndIndex] != ':' || depth >= MaxNestingDepth)
			return false;

		// ${n:default} - the default text may contain nested placeholders.
		int textStart = builder.Length;

		AppendExpanded(builder, placeholders, snippet, startIndex: digitEndIndex + 1, endIndex: closeIndex, depth: depth + 1);

		placeholders.Add(new(tabstopIndex, TextRange.FromBounds(textStart, builder.Length), null));
		return true;
	}

	/// <summary>Expands a choice placeholder whose text runs from the first choice to the first unescaped <c>|}</c> terminator.</summary>
	/// <param name="builder">The builder receiving the visible text (the first choice).</param>
	/// <param name="placeholders">The list receiving the placeholder.</param>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="tabstopIndex">The parsed tabstop index.</param>
	/// <param name="bodyStartIndex">The index of the first choice character.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <param name="choiceEndIndex">
	/// Receives the exclusive end of the expanded choice construct (the index after the terminating <c>|}</c>) when the method
	/// returns <see langword="true"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the body is a well-formed choice list: at least one element, every element non-empty, terminated by <c>|}</c>.
	/// <see langword="false"/> when the terminating <c>|}</c> is missing, an element is empty, or a <c>|</c> that is not the terminator appears;
	/// the construct must stay literal.
	/// </returns>
	private static bool TryExpandChoice(
		StringBuilder builder,
		List<SnippetPlaceholder> placeholders,
		string snippet,
		int tabstopIndex,
		int bodyStartIndex,
		int endIndex,
		out int choiceEndIndex
	)
	{
		var choices = new List<string>();
		var choice = new StringBuilder();

		int index = bodyStartIndex;

		while (index < endIndex)
		{
			char character = snippet[index];

			// Choice text resolves \, and \| on top of the shared snippet escapes; any other
			// escape keeps its backslash.
			if (TryConsumeEscape(snippet, ref index, endIndex, choiceEscapes: true, target: choice))
				continue;

			if (character == ',')
			{
				// Empty choice elements are not part of the supported subset: a choice with an empty
				// element is not a valid choice, so the whole construct stays literal.
				if (choice.Length == 0)
				{
					choiceEndIndex = 0;
					return false;
				}

				choices.Add(choice.ToString());
				choice.Clear();
				index++;
				continue;
			}

			if (character == '|')
			{
				// Only "|}" terminates the choice list; any other '|' makes the construct malformed.
				if (!SnippetSyntaxProbe.IsChoiceTerminatorAt(snippet, index, endIndex))
				{
					choiceEndIndex = 0;
					return false;
				}

				if (choice.Length == 0)
				{
					choiceEndIndex = 0;
					return false;
				}

				choices.Add(choice.ToString());

				int textStart = builder.Length;

				builder.Append(choices[0]);
				placeholders.Add(new(tabstopIndex, TextRange.FromBounds(textStart, builder.Length), choices));
				choiceEndIndex = index + 2;
				return true;
			}

			// A '}' that is not preceded by '|' is plain choice text.
			choice.Append(character);
			index++;
		}

		choiceEndIndex = 0;
		return false;
	}

	/// <summary>
	/// Finds the close brace of the construct that starts at the supplied content index, skipping escaped characters, nested <c>${</c> constructs,
	/// and well-formed choice segments (whose text may contain <c>}</c>). The scan mirrors the expansion path: a choice whose text
	/// is not terminated by <c>|}</c>, or that contains a <c>|</c> without a following <c>}</c>, is not a construct and does not consume braces.
	/// </summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="contentStartIndex">The index of the first construct body character.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <param name="hasCloseBrace">
	/// Receives whether the scanned range contains any <c>}</c> character. When this value is <see langword="false"/>, no construct in the remainder
	/// of the range can close, so the caller can process the remainder in one pass.
	/// </param>
	/// <returns>The index of the matching close brace, or <c>-1</c> when the construct is unterminated.</returns>
	private static int FindConstructEnd(string snippet, int contentStartIndex, int endIndex, out bool hasCloseBrace)
	{
		int depth = 1;
		int index = contentStartIndex;

		bool sawCloseBrace = false;

		while (index < endIndex)
		{
			char character = snippet[index];

			if (SnippetSyntaxProbe.IsEscapeAt(snippet, index, endIndex))
			{
				if (SnippetSyntaxProbe.EscapesCloseBrace(snippet, index))
					sawCloseBrace = true;

				index += 2;
				continue;
			}

			if (SnippetSyntaxProbe.IsBraceOpenAt(snippet, index, endIndex))
			{
				// A choice nested in the construct is skipped as a unit because its text may
				// contain '}' that must not count as a close brace.
				if (SnippetSyntaxProbe.IsChoiceHeadAt(snippet, contentStartIndex: index + 2, endIndex, out int digitEndIndex))
				{
					int terminatorIndex = FindChoiceTerminator(
						snippet,
						startIndex: digitEndIndex + 1,
						endIndex,
						hasCloseBrace: out bool choiceSawCloseBrace
					);

					sawCloseBrace |= choiceSawCloseBrace;

					if (terminatorIndex >= 0)
					{
						index = terminatorIndex + 2;
						continue;
					}

					// An unterminated choice is not a construct boundary: the '$' is plain text and
					// scanning resumes at the '{', mirroring the expansion path so both agree on
					// where the enclosing construct ends.
					index++;
					continue;
				}

				// A nested construct opens another level; a literal { is plain text.
				depth++;
				index += 2;
				continue;
			}

			if (character == '}')
			{
				sawCloseBrace = true;
				depth--;

				if (depth == 0)
				{
					hasCloseBrace = sawCloseBrace;
					return index;
				}
			}

			index++;
		}

		hasCloseBrace = sawCloseBrace;
		return -1;
	}

	/// <summary>
	/// Finds the <c>|</c> of the first unescaped <c>|}</c> terminator in a choice body. The expansion path rejects a choice whose first unescaped
	/// <c>|</c> is not followed by <c>}</c> as malformed, so this scan reports no terminator in that case as well.
	/// </summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="startIndex">The index of the first choice character.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <param name="hasCloseBrace">
	/// Receives whether the scanned range contains any <c>}</c> character, including characters that were skipped as escapes.
	/// </param>
	/// <returns>The index of the terminating <c>|</c>, or <c>-1</c> when the choice is unterminated.</returns>
	private static int FindChoiceTerminator(string snippet, int startIndex, int endIndex, out bool hasCloseBrace)
	{
		int index = startIndex;
		bool sawCloseBrace = false;

		while (index < endIndex)
		{
			if (SnippetSyntaxProbe.IsEscapeAt(snippet, index, endIndex))
			{
				if (SnippetSyntaxProbe.EscapesCloseBrace(snippet, index))
					sawCloseBrace = true;

				index += 2;
				continue;
			}

			if (snippet[index] == '|')
			{
				if (SnippetSyntaxProbe.IsChoiceTerminatorAt(snippet, index, endIndex))
				{
					hasCloseBrace = true;
					return index;
				}

				hasCloseBrace = sawCloseBrace;
				return -1;
			}

			if (snippet[index] == '}')
				sawCloseBrace = true;

			index++;
		}

		hasCloseBrace = sawCloseBrace;
		return -1;
	}

	/// <summary>Records a zero-length tabstop at the current end of the visible text.</summary>
	/// <param name="builder">The builder holding the visible text so far.</param>
	/// <param name="placeholders">The list receiving the placeholder.</param>
	/// <param name="tabstopIndex">The parsed tabstop index.</param>
	private static void AddTabstop(StringBuilder builder, List<SnippetPlaceholder> placeholders, int tabstopIndex) =>
		placeholders.Add(new(tabstopIndex, new TextRange(builder.Length, 0), null));
}
