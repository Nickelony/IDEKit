using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Tests.TestSupport;
using System.Globalization;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
// The Turkish-culture test mutates the process-wide culture, so this class must not run in parallel.
[DoNotParallelize]
public sealed class TextCompletionFilterTests
{
	private static readonly IReadOnlyList<TextCompletionItem> s_items = CompletionTestCatalog.Items;

	[TestMethod]
	public void FilterByWord_EmptyItems_ReturnsEmpty()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord([], "Alpha");

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void FilterByWord_NullElement_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
			() => TextCompletionFilter.FilterByWord([null!], "Alpha"));

		Assert.AreEqual("items", exception.ParamName);
		StringAssert.Contains(exception.Message, "null element at index 0");
	}

	[TestMethod]
	public void FilterByWord_MultipleMatches_PreserveInputOrderAndItemIdentity()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord(s_items, "a");

		Assert.AreEqual(3, result.Count);
		Assert.AreSame(s_items[0], result[0]);
		Assert.AreSame(s_items[1], result[1]);
		Assert.AreSame(s_items[2], result[2]);
	}

	[TestMethod]
	public void FilterByWord_WhitespaceOnlyWord_MatchesFilterTextLiterally()
	{
		// A whitespace-only word is matched literally rather than treated as empty; only an item whose
		// filter text contains the whitespace matches.
		var item = new TextCompletionItem("display") { FilterText = "Beta: " };

		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord([item], " ");

		Assert.AreEqual(1, result.Count);
	}

	[TestMethod]
	public void FilterByWord_EmptyWord_ReturnsCallerOwnedCopyOfAllItems()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord(s_items, string.Empty);

		Assert.AreNotSame(s_items, result);
		CollectionAssert.AreEqual(s_items.ToArray(), result.ToArray());
	}

	[TestMethod]
	public void FilterByWord_UnderTurkishCulture_KeepsOrdinalCaseInsensitiveMatching()
	{
		// Turkish maps 'I' and 'i' to different letters; the ordinal contract must keep ASCII casing
		// variants matching regardless of the current culture.
		var previousCulture = CultureInfo.CurrentCulture;
		var previousUiCulture = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
			CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

			IReadOnlyList<TextCompletionItem> items = [new TextCompletionItem("Item") { InsertText = "Item" }];

			IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord(items, "item");

			Assert.AreEqual(1, result.Count);
			Assert.AreEqual("Item", result[0].InsertText);
		}
		finally
		{
			CultureInfo.CurrentCulture = previousCulture;
			CultureInfo.CurrentUICulture = previousUiCulture;
		}
	}

	[TestMethod]
	public void FilterByWord_ContainsMatch_IsCaseInsensitive()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord(s_items, "ET");

		Assert.AreEqual(1, result.Count);
		Assert.AreEqual("Beta: ", result[0].InsertText);
	}

	[TestMethod]
	public void FilterByWord_NonAsciiCaseVariant_MatchesUnderOrdinalFolding()
	{
		// Ordinal case-insensitivity folds through the invariant casing table, so a non-ASCII
		// character keeps matching its case variant instead of depending on the current culture.
		var item = new TextCompletionItem("display") { FilterText = "Caf\u00E9" };

		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord([item], "CAF\u00C9");

		Assert.AreEqual(1, result.Count);
		Assert.AreSame(item, result[0]);
	}

	[TestMethod]
	public void FilterByWord_NoMatch_ReturnsAndReusesTheSharedEmptyResult()
	{
		// The no-match refresh path returns one shared empty list instead of allocating per call.
		IReadOnlyList<TextCompletionItem> first = TextCompletionFilter.FilterByWord(s_items, "Zzz");
		IReadOnlyList<TextCompletionItem> second = TextCompletionFilter.FilterByWord(s_items, "Zzz");

		Assert.AreEqual(0, first.Count);
		Assert.AreSame(first, second);
	}

	[TestMethod]
	public void FilterByWord_MatchesFilterTextWhenInsertTextDiffers()
	{
		var item = new TextCompletionItem("display") { InsertText = "inserted", FilterText = "aliased" };

		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord([item], "alias");

		Assert.AreEqual(1, result.Count);
	}

	[TestMethod]
	public void FilterByWord_DoesNotMatchInsertText()
	{
		// The match rule covers filter text and its label fallback only; commit text must not widen it.
		var aliased = new TextCompletionItem("display") { InsertText = "inserted", FilterText = "aliased" };

		Assert.AreEqual(0, TextCompletionFilter.FilterByWord([aliased], "sert").Count);

		// The catalog's Beta item inserts "Beta: " while its label is "Beta"; a word that only the
		// insertion text contains selects nothing either.
		Assert.AreEqual(0, TextCompletionFilter.FilterByWord(s_items, "a: ").Count);
	}
}
