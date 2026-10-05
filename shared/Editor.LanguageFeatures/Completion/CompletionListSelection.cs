using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using PointerClickArgs = Avalonia.Input.PointerReleasedEventArgs;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Text;
using System.Windows.Controls;
using PointerClickArgs = System.Windows.Input.MouseButtonEventArgs;
#endif
#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif
/// <summary>Selects items in a completion window: the clicked item, the best match for the typed word, and the provider-preselected item.</summary>
internal static class CompletionListSelection
{
	// A query that cannot match a completion item; it invalidates the completion list's memoized query (see ResetFilter).
	private const string FilterResetQuery = "\0";

	/// <summary>Selects the item a click landed on and scrolls it into view.</summary>
	/// <param name="listBox">The completion-list list box that received the click.</param>
	/// <param name="e">The click event.</param>
	/// <remarks>
	/// The non-activatable window keeps the text area's keyboard focus, so the click selects the item explicitly; selecting a different
	/// item raises SelectionChanged, which schedules the tooltip update.
	/// </remarks>
	internal static void SelectClickedItem(ListBox listBox, PointerClickArgs e)
	{
		object? item = CompletionSeam.FindClickedItem(listBox, e);

		if (item is null)
			return;

		if (!ReferenceEquals(listBox.SelectedItem, item))
			listBox.SelectedItem = item;

		listBox.ScrollIntoView(item);
	}

	/// <summary>Replaces the items of the window's list.</summary>
	/// <param name="completionWindow">The window whose list receives the items.</param>
	/// <param name="items">The items the list shows afterwards.</param>
	internal static void ReplaceItems(CompletionWindow completionWindow, ICompletionData[] items)
	{
		IList<ICompletionData> completionData = completionWindow.CompletionList.CompletionData;
		completionData.Clear();

		foreach (ICompletionData item in items)
			completionData.Add(item);
	}

	/// <summary>Selects the best match for the typed word and applies the provider's preselection.</summary>
	/// <param name="completionWindow">The window whose list is selected.</param>
	/// <param name="textArea">The text area whose document holds the typed word.</param>
	internal static void SelectInitialItem(CompletionWindow completionWindow, TextArea textArea)
	{
		completionWindow.CompletionList.SelectItem(GetQuery(completionWindow, textArea));
		ApplyPreselection(completionWindow);
	}

	/// <summary>Re-filters the list against the typed word after its item set was replaced, then applies the provider's preselection.</summary>
	/// <param name="completionWindow">The window whose list is refreshed.</param>
	/// <param name="textArea">The text area whose document holds the typed word.</param>
	/// <remarks>
	/// The completion list memoizes the query it last applied, returns early for the same one, and filters from the item
	/// view it held when that query ran. A refresh that replaced the item set under an unchanged
	/// query would therefore early-return and keep the previous items visible. The reset query - a NUL, which no completion label contains,
	/// so it can neither match an item nor extend the memoized query - invalidates that memo, and the real query then re-filters the replaced items.
	/// Both behaviors are the engine's own, not a documented contract: re-check the sequence against the engine source.
	/// </remarks>
	internal static void RefreshFilter(CompletionWindow completionWindow, TextArea textArea)
	{
		CompletionList completionList = completionWindow.CompletionList;
		string query = GetQuery(completionWindow, textArea);

		completionList.SelectItem(FilterResetQuery);
		completionList.SelectItem(query);

		ApplyPreselection(completionWindow);
	}

	// A provider may mark one item as preselected (LSP's "preselect"); a surviving marked item becomes the
	// selection, because it is the server's explicit answer to the query, and without a hint the library keeps
	// the engine's best-match selection. The flag is read through ICompletionPreselection, so the default
	// adapter and custom host data follow the same policy. The hint is re-applied on an in-place refresh too,
	// because a refresh replaces the item set and a replaced set cannot carry the prior selection. The
	// selection is scrolled in explicitly because the engine's selected-item setter does not scroll.
	private static void ApplyPreselection(CompletionWindow completionWindow)
	{
		ItemCollection items = completionWindow.CompletionList.ListBox.Items;

		for (int index = 0; index < items.Count; index++)
		{
			if (items[index] is not ICompletionData completionItem)
				continue;

			if (completionItem is not ICompletionPreselection { IsPreselected: true })
				continue;

			completionWindow.CompletionList.ListBox.SelectedItem = completionItem;
			completionWindow.CompletionList.ScrollIntoView(completionItem);
			return;
		}
	}

	private static string GetQuery(CompletionWindow completionWindow, TextArea textArea)
	{
		// A detached document has no text to query, so the query is empty; otherwise, both offsets are
		// clamped to the current document so an edit between the window update and this query cannot
		// produce an out-of-range read.
		TextDocument? document = textArea.Document;

		if (document is null)
			return string.Empty;

		int startOffset = document.ClampOffset(completionWindow.StartOffset);
		int endOffset = Math.Max(startOffset, document.ClampOffset(completionWindow.EndOffset));

		return endOffset > startOffset ? document.GetText(startOffset, endOffset - startOffset) : string.Empty;
	}
}
