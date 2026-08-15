#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using ImageSource = Avalonia.Media.IImage;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageSource = System.Windows.Media.ImageSource;
#endif
using Nickelony.IDEKit.IntelliSense.Completion;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Adapts a shared <see cref="TextCompletionItem"/> to the editor's <see cref="ICompletionData"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// This is the default completion data the <see cref="TextCompletionController"/> maps decision items to
/// when the host supplies no item factory: the label is the displayed content, the item's description feeds
/// the tooltip, the item's priority feeds the editor's selection ranking, and committing replaces the
/// replacement range with the item's commit text: the edit payload's replacement text when the item
/// carries one (the edit supersedes the plain insertion text), and the insertion text otherwise (the
/// label when no explicit insertion text is set). The edit's own range is used for the primary
/// replacement while it still fits the current document, with the completion window's segment as the
/// fallback for a missing or stale range. A <see cref="TextCompletionInsertTextFormat.Snippet"/> commit
/// text is expanded with <see cref="TextSnippetExpander"/> before it is inserted, and the caret lands
/// after the final tabstop's text when the snippet carries one; navigating the remaining tabstops stays
/// host behavior. The item's additional text edits are applied together with the insertion inside one
/// document update, so a commit that carries secondary data is still one undo unit.
/// </para>
/// <para>
/// The adapter is deliberately minimal: it renders no icon and no detail layout, and it does not apply
/// protocol sort text because the editor ranks by <see cref="Priority"/> and match quality only. Three item
/// facts feed behavior instead of markup: a deprecated item's label renders struck through (see
/// <see cref="Content"/>), the item's commit characters are exposed through
/// <see cref="ICommitCharacterCompletionData"/> so the controller's commit-character policy applies without
/// a host item, and the item's preselection flag is exposed through
/// <see cref="IPreselectedCompletionData"/> so the controller selects such an item over the best match.
/// Hosts that style their items or map additional protocol fields supply their own
/// <see cref="ICompletionData"/> implementation through the
/// <see cref="TextCompletionControllerHooks.CompletionItemFactory"/> hook instead.
/// </para>
/// <para>
/// The editor filters and ranks the visible items against <see cref="ICompletionData.Text"/>; the adapter
/// sets that property to the item's <see cref="TextCompletionItem.FilterText"/> (which falls back to the
/// label when the item declares none), the same key the shared completion-session kernel matches. An item
/// whose label carries decoration the filter text does not - for example a callable label such as
/// <c>foo(a, b)</c> with the filter text <c>foo</c> - therefore matches a query for <c>foo</c>, and the
/// decorated label stays the displayed content instead of being changed to what the query matches.
/// </para>
/// </remarks>
public sealed class TextCompletionItemCompletionData : ICompletionData, ICommitCharacterCompletionData, IPreselectedCompletionData
{
	private readonly bool _isDeprecated;
	private TextBlock? _deprecatedContent;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionItemCompletionData"/> class.
	/// </summary>
	/// <param name="item">The completion item to present.</param>
	/// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
	public TextCompletionItemCompletionData(TextCompletionItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		Item = item;
		_isDeprecated = ContainsDeprecatedTag(item.Tags);
	}

	/// <summary>
	/// Gets the shared completion item this adapter presents.
	/// </summary>
	public TextCompletionItem Item { get; }

	/// <inheritdoc/>
	public ImageSource? Image => null;

	/// <inheritdoc/>
	/// <remarks>
	/// The text is the item's <see cref="TextCompletionItem.FilterText"/>, which falls back to the label
	/// when the item declares none, so the editor filters and ranks against the same key the shared
	/// completion-session kernel matches. The displayed content stays the faithful label
	/// (see <see cref="Content"/>).
	/// </remarks>
	public string Text => Item.FilterText;

	/// <inheritdoc/>
	/// <remarks>
	/// A deprecated item (<see cref="TextCompletionTag.Deprecated"/> in the item's
	/// <see cref="TextCompletionItem.Tags"/>) renders its label struck through: the content is a
	/// <see cref="TextBlock"/> with strikethrough text decorations, so the item stays as selectable and
	/// committable as any other entry. Every other item keeps the plain label string. The
	/// <see cref="TextBlock"/> is created on the first content read - the owner thread that renders the
	/// completion list - rather than in the constructor, so an adapter built off the UI thread (for example
	/// by a host item factory after an asynchronous request) never creates an object with UI-thread affinity
	/// on a thread-pool thread.
	/// </remarks>
	public object Content
	{
		get
		{
			if (!_isDeprecated)
				return Item.Label;

			// The element is created once and stored, so every content read returns the same instance,
			// like the label string a plain item returns.
			return _deprecatedContent ??= new TextBlock
			{
				Text = Item.Label,
				TextDecorations = TextDecorations.Strikethrough
			};
		}
	}

	/// <inheritdoc/>
	public object? Description => Item.Documentation;

	/// <inheritdoc/>
	public double Priority => Item.Priority;

	/// <inheritdoc/>
	public IReadOnlyList<string> CommitCharacters => Item.CommitCharacters;

	/// <inheritdoc/>
	public bool IsPreselected => Item.IsPreselected;

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="completionSegment"/> is <see langword="null"/>.
	/// </exception>
	public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(completionSegment);

		// The edit payload supersedes the plain insertion text (LSP text-edit semantics), so its
		// replacement text is what gets committed when the item carries one.
		string insertText = Item.TextEdit?.NewText ?? Item.InsertText;
		int? caretOffset = null;

		if (Item.InsertTextFormat == TextCompletionInsertTextFormat.Snippet)
		{
			TextSnippetExpansion expansion = TextSnippetExpander.Expand(insertText);
			insertText = expansion.Text;

			// Land the caret after the final tabstop's text when the snippet carries one; tab
			// navigation across the remaining placeholders stays host behavior.
			foreach (TextSnippetPlaceholder placeholder in expansion.Placeholders)
			{
				if (placeholder.Index == 0)
				{
					caretOffset = placeholder.Range.EndOffset;
					break;
				}
			}
		}

		CompletionCommitEditApplier.Apply(new CompletionCommitPayload(
			TextArea: textArea,
			CompletionSegment: completionSegment,
			InsertText: insertText,
			CaretOffsetInInsertText: caretOffset,
			AdditionalTextEdits: Item.AdditionalTextEdits,
			PrimaryEdit: Item.TextEdit));
	}

	private static bool ContainsDeprecatedTag(IReadOnlyList<TextCompletionTag> tags)
	{
		for (int index = 0; index < tags.Count; index++)
		{
			if (tags[index] == TextCompletionTag.Deprecated)
				return true;
		}

		return false;
	}
}
