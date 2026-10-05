using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Snippets;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Snippets;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Builds the editor's <see cref="Snippet"/> for an expanded completion snippet, so the commit can step through its tabstops.</summary>
/// <remarks>
/// <para>
/// The snippet inserts the expansion's text. Each tabstop other than the final one becomes a replaceable element, and every other occurrence
/// of its index becomes a bound element that mirrors it, so it inserts that element's text in place of its own. The replaceable element
/// is the first occurrence that has text, or the first occurrence when none has. The final tabstop, or the end of the text
/// when there is none, becomes a caret element, where the caret lands when the snippet ends with Enter.
/// </para>
/// <para>
/// The editor visits the replaceable elements in text order, which is not the order of their indexes, and cannot step between two that touch,
/// so an expansion with touching tabstops builds no snippet.
/// </para>
/// </remarks>
internal static class CompletionSnippetBuilder
{
	/// <summary>Builds the snippet of an expansion.</summary>
	/// <param name="expansion">The expansion of the snippet text that the commit inserts.</param>
	/// <returns>
	/// The snippet, or <see langword="null"/> when the expansion has no tabstop besides the final one, when two placeholders overlap,
	/// because the editor's elements are flat and cannot hold a placeholder inside another, or when two tabstops touch, because the editor's Tab
	/// selects the first tabstop that starts after the caret, which is never the one that starts where the previous one ends.
	/// </returns>
	internal static Snippet? Build(SnippetExpansion expansion)
	{
		string text = expansion.Text;

		List<SnippetPlaceholder> placeholders =
		[
			.. expansion.Placeholders.OrderBy(static placeholder => placeholder.Range.Offset).ThenBy(static placeholder => placeholder.Range.Length),
		];

		if (!placeholders.Exists(static placeholder => placeholder.Index != 0) || !AreSeparateWithin(placeholders, text.Length))
			return null;

		Dictionary<int, int> targetOf = FindTargets(placeholders);

		var elements = targetOf.ToDictionary(
			static pair => pair.Key,

			pair => new SnippetReplaceableTextElement
			{
				Text = text.Substring(placeholders[pair.Value].Range.Offset, placeholders[pair.Value].Range.Length),
			}
		);

		var snippet = new Snippet();

		bool hasCaret = false;
		int position = 0;

		// The offsets below count the text the snippet has inserted so far, which a mirror lengthens, so they differ from the expansion's.
		int inserted = 0;
		int editableEnd = -1;
		int placeholderEnd = -1;

		for (int index = 0; index < placeholders.Count; index++)
		{
			SnippetPlaceholder placeholder = placeholders[index];
			string between = text[position..placeholder.Range.Offset];

			AddText(snippet, between);
			inserted += between.Length;

			string placeholderText = text.Substring(placeholder.Range.Offset, placeholder.Range.Length);
			position = placeholder.Range.EndOffset;

			if (placeholder.Index == 0)
			{
				AddText(snippet, placeholderText);
				inserted += placeholderText.Length;

				if (!hasCaret)
				{
					snippet.Elements.Add(new CaretElement(movesAfterInsertion: inserted == placeholderEnd));
					hasCaret = true;
				}

				continue;
			}

			SnippetReplaceableTextElement target = elements[placeholder.Index];

			if (targetOf[placeholder.Index] == index)
			{
				// The editor's Tab selects the first tabstop that starts after the caret, so a tabstop that starts where the previous one ends is unreachable.
				if (inserted == editableEnd)
					return null;

				snippet.Elements.Add(target);
				inserted += target.Text.Length;
				editableEnd = inserted;
			}
			else
			{
				// A mirror holds no text of its own: the editor inserts its target's text, and the target may come later in the snippet.
				snippet.Elements.Add(new SnippetBoundElement { TargetElement = target });
				inserted += target.Text.Length;
			}

			placeholderEnd = inserted;
		}

		AddText(snippet, text[position..]);
		inserted += text.Length - position;

		if (!hasCaret)
			snippet.Elements.Add(new CaretElement(movesAfterInsertion: inserted == placeholderEnd));

		return snippet;
	}

	// The index in the text-ordered list of each tabstop's replaceable occurrence: the first one that has text, or the first one without.
	private static Dictionary<int, int> FindTargets(List<SnippetPlaceholder> placeholders)
	{
		var targetOf = new Dictionary<int, int>();

		for (int index = 0; index < placeholders.Count; index++)
		{
			SnippetPlaceholder placeholder = placeholders[index];

			if (placeholder.Index == 0)
				continue;

			if (!targetOf.TryGetValue(placeholder.Index, out int current) || (placeholders[current].Range.IsEmpty && !placeholder.Range.IsEmpty))
				targetOf[placeholder.Index] = index;
		}

		return targetOf;
	}

	// Placeholders in text order are separate when each starts at or after the end of the one before and the last ends within the text.
	private static bool AreSeparateWithin(List<SnippetPlaceholder> placeholders, int textLength)
	{
		int end = 0;

		foreach (SnippetPlaceholder placeholder in placeholders)
		{
			if (placeholder.Range.Offset < end || !placeholder.Range.FitsWithin(textLength))
				return false;

			end = placeholder.Range.EndOffset;
		}

		return true;
	}

	private static void AddText(Snippet snippet, string text)
	{
		if (text.Length > 0)
			snippet.Elements.Add(new SnippetTextElement { Text = text });
	}

	/// <summary>Marks where the caret lands when the snippet ends with Enter.</summary>
	/// <remarks>
	/// The editor's own caret element keeps its anchor before text inserted at it, so text typed into a tabstop that ends where the caret stands
	/// would push the tabstop's end past the anchor. This element moves its anchor after inserted text when it stands where a tabstop ends.
	/// </remarks>
	internal sealed class CaretElement : SnippetElement
	{
		/// <param name="movesAfterInsertion">Whether text inserted at the caret position goes before the caret.</param>
		internal CaretElement(bool movesAfterInsertion) => MovesAfterInsertion = movesAfterInsertion;

		/// <summary>Whether text inserted at the caret position goes before the caret.</summary>
		internal bool MovesAfterInsertion { get; }

		/// <inheritdoc/>
		public override void Insert(InsertionContext context)
		{
			TextAnchor anchor = context.Document.CreateAnchor(context.InsertionPosition);

			// The rest of the snippet is inserted at this offset after the anchor exists, and an anchor that moves after inserted text would be
			// pushed past it, so the anchor takes its movement once the insertion is complete.
			anchor.MovementType = AnchorMovementType.BeforeInsertion;
			anchor.SurviveDeletion = true;

			if (MovesAfterInsertion)
				context.InsertionCompleted += (_, _) => anchor.MovementType = AnchorMovementType.AfterInsertion;

			context.Deactivated += (_, e) =>
			{
				if (e.Reason == DeactivateReason.ReturnPressed)
					context.TextArea.Caret.Offset = anchor.Offset;
			};
		}
	}
}
