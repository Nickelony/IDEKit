using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;
#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Input;
using ImageSource = Avalonia.Media.IImage;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageSource = System.Windows.Media.ImageSource;
using TextInputEventArgs = System.Windows.Input.TextCompositionEventArgs;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Adapts a shared <see cref="CompletionItem"/> to the editor's <see cref="ICompletionData"/> contract.</summary>
/// <remarks>
/// <para>
/// This is the default completion data the <see cref="CompletionPresenter"/> maps decision items to when the host supplies no item factory.
/// <list type="bullet">
/// <item>
/// The label is the displayed content, the item's description feeds the tooltip, and the item's priority feeds the editor's selection ranking.
/// </item>
/// <item>Committing replaces the replacement range with the commit text.</item>
/// <item>The commit text is the edit's replacement text when the item carries one, which supersedes the insertion text.</item>
/// <item>Otherwise the commit text is the insertion text, or the label when no explicit insertion text is set.</item>
/// <item>
/// The edit's own range is used for the primary replacement while it still fits the current document, and the completion window's segment
/// is the fallback for a missing or stale range.
/// </item>
/// <item>
/// A <see cref="CompletionInsertTextFormat.Snippet"/> commit text is expanded
/// with <see cref="Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets.SnippetExpander"/> before it is inserted, and the caret lands
/// after the final tabstop's text when the snippet carries one.
/// </item>
/// <item>
/// A snippet with a tabstop besides the final one is inserted through the editor's snippet instead, which selects the first
/// tabstop and lets Tab step to the next. The insertion is plain text when <see cref="CompletionItemAdapterHooks.AdjustInsertion"/> changes its text,
/// when tabstops nest or touch, and when a typed commit character commits the item.
/// </item>
/// <item>
/// The item's additional text edits are applied together with the insertion inside one document update, so a commit that carries secondary data
/// is still one undo unit.
/// </item>
/// </list>
/// </para>
/// <para>
/// The adapter is deliberately minimal: it renders no detail layout, and it does not apply protocol sort text because the editor
/// ranks by <see cref="Priority"/> and match quality only.
/// Item facts feed behavior instead of markup:
/// <list type="bullet">
/// <item>A deprecated item's label renders struck through (see <see cref="Content"/>).</item>
/// <item>
/// The item's commit characters are exposed through <see cref="ICompletionCommitCharacters"/>, so the presenter's commit-character policy applies
/// without a host item.
/// </item>
/// <item>
/// The item's preselection flag is exposed through <see cref="ICompletionPreselection"/>, so the presenter selects such an item over the best match.
/// </item>
/// </list>
/// A host that draws item icons, declines a stale commit or adjusts the insertion passes <see cref="CompletionItemAdapterHooks"/> and keeps this
/// commit path, additional text edits included. Hosts that style their items or map additional protocol fields supply their own
/// <see cref="ICompletionData"/> implementation through the <see cref="CompletionPresenterHooks.CompletionItemFactory"/> hook instead.
/// </para>
/// <para>
/// The editor filters and ranks the visible items against <see cref="ICompletionData.Text"/>, which the adapter sets to the item's
/// <see cref="CompletionItem.FilterText"/>, the same key the shared completion-session kernel matches.
/// The filter text falls back to the label when the item declares none.
/// An item whose label carries decoration the filter text does not, such as a callable label <c>foo(a, b)</c> with the filter text <c>foo</c>,
/// therefore matches a query for <c>foo</c>. The decorated label stays the displayed content instead of being changed to what the query matches.
/// </para>
/// </remarks>
public sealed class CompletionItemAdapter : ICompletionData, ICompletionCommitCharacters, ICompletionPreselection
{
	private readonly bool _isDeprecated;
	private readonly CompletionItemAdapterHooks? _hooks;

	private TextBlock? _deprecatedContent;

	/// <param name="item">The completion item to present.</param>
	/// <param name="hooks">The host hooks of the icon, the commit gate and the insertion, or <see langword="null"/> when the host needs none.</param>
	/// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
	public CompletionItemAdapter(CompletionItem item, CompletionItemAdapterHooks? hooks = null)
	{
		ArgumentNullException.ThrowIfNull(item);

		Item = item;
		_hooks = hooks;
		_isDeprecated = ContainsDeprecatedTag(item.Tags);
	}

	/// <summary>The shared completion item this adapter presents.</summary>
	public CompletionItem Item { get; }

	/// <inheritdoc/>
	/// <remarks>
	/// The image is what <see cref="CompletionItemAdapterHooks.ImageFor"/> returns for the item, or <see langword="null"/> without the hook.
	/// </remarks>
	public ImageSource? Image => _hooks?.ImageFor?.Invoke(Item);

	/// <inheritdoc/>
	/// <remarks>
	/// The text is the item's <see cref="CompletionItem.FilterText"/>, which falls back to the label when the item declares none, so the editor
	/// filters and ranks against the same key the shared completion-session kernel matches. The displayed content stays the faithful label
	/// (see <see cref="Content"/>).
	/// </remarks>
	public string Text => Item.FilterText;

	/// <inheritdoc/>
	/// <remarks>
	/// A deprecated item (<see cref="CompletionItemTag.Deprecated"/> in the item's <see cref="CompletionItem.Tags"/>)
	/// renders its label struck through: the content is a <see cref="TextBlock"/> with strikethrough text decorations, so the item
	/// stays as selectable and committable as any other entry. Every other item keeps the plain label string. The <see cref="TextBlock"/> is created
	/// on the first content read - the owner thread that renders the completion list - rather than in the constructor, so an adapter
	/// built off the UI thread (for example by a host item factory after an asynchronous request) never creates an object with UI-thread affinity
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
			return _deprecatedContent ??= new TextBlock { Text = Item.Label, TextDecorations = TextDecorations.Strikethrough };
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The text of the item's documentation, whatever its markup kind. A binding with a completion tooltip reads the kind from <see cref="Item"/>
	/// to render Markdown, so this value never carries it.
	/// </remarks>
	public object? Description => Item.Documentation?.Value;

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
	/// <exception cref="InvalidOperationException">
	/// The text area has no document, or <see cref="CompletionItemAdapterHooks.AdjustInsertion"/> returned an insertion without text.
	/// </exception>
	/// <remarks>
	/// A <see cref="CompletionItemAdapterHooks.CanCommit"/> answer of <see langword="false"/> leaves the document unchanged.
	/// The <see cref="CompletionItemAdapterHooks.AdjustInsertion"/> hook runs after the insertion is resolved and before the commit is planned.
	/// </remarks>
	public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(completionSegment);

		if (_hooks?.CanCommit?.Invoke(Item) == false)
			return;

		SnippetExpansion? snippet = CompletionInsertionResolver.ResolveSnippet(Item);

		CompletionInsertion insertion = snippet is { } expansion
			? CompletionInsertionResolver.Resolve(expansion)
			: CompletionInsertionResolver.Resolve(Item);

		CompletionCommitEditApplier.Apply(
			new CompletionCommitRequest(
				textArea,
				completionSegment,
				InsertText: insertion.Text,
				CaretOffsetInInsertText: insertion.CaretOffset,
				AdditionalTextEdits: Item.AdditionalTextEdits,
				PrimaryEdit: Item.TextEdit,
				AdjustInsertion: _hooks?.AdjustInsertion,

				// A typed commit character reaches the document after the commit, so a snippet commit by text input leaves the caret at the final
				// tabstop, as a plain insertion does, where selecting the first tabstop would let the character overwrite it.
				Snippet: insertionRequestEventArgs is TextInputEventArgs ? null : snippet
			)
		);
	}

	private static bool ContainsDeprecatedTag(IReadOnlyList<CompletionItemTag> tags)
	{
		for (int index = 0; index < tags.Count; index++)
		{
			if (tags[index] == CompletionItemTag.Deprecated)
				return true;
		}

		return false;
	}

	/// <summary>Maps shared completion items to engine completion data through a host factory or this adapter.</summary>
	/// <param name="items">The items to map.</param>
	/// <param name="itemFactory">The host factory, or <see langword="null"/> to wrap each item in a <see cref="CompletionItemAdapter"/>.</param>
	/// <param name="hooks">The hooks of each adapter this method creates; a host factory receives none.</param>
	/// <returns>The completion data in the order of <paramref name="items"/>.</returns>
	/// <exception cref="InvalidOperationException"><paramref name="itemFactory"/> returned <see langword="null"/> for an item.</exception>
	internal static ICompletionData[] CreateAll(
		IReadOnlyList<CompletionItem> items,
		Func<CompletionItem, ICompletionData>? itemFactory,
		CompletionItemAdapterHooks? hooks
	)
	{
		var data = new ICompletionData[items.Count];

		for (int i = 0; i < items.Count; i++)
		{
			// A null return violates the factory contract in a way no caller can act on as a parameter
			// error, so it is reported as an invalid operation naming the offending item.
			data[i] = itemFactory is null
				? new CompletionItemAdapter(items[i], hooks)
				: itemFactory(items[i])
					?? throw new InvalidOperationException($"The completion item factory returned null for the item '{items[i].Label}'.");
		}

		return data;
	}
}
