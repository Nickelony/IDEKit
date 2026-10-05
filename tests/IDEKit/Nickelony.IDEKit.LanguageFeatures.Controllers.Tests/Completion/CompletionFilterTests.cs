using System.Globalization;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
// The Turkish-culture test mutates the process-wide culture, so this class must not run in parallel.
[DoNotParallelize]
public sealed class CompletionFilterTests
{
	private static readonly IReadOnlyList<CompletionItem> s_items = TestCompletionCatalog.Items;

	[TestMethod]
	public void FilterByWord_EmptyItems_ReturnsEmpty()
	{
		IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord([], "Alpha");
		Assert.IsEmpty(result);
	}

	[TestMethod]
	public void FilterByWord_NullElement_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => CompletionFilter.FilterByWord([null!], "Alpha"));

		Assert.AreEqual("items", exception.ParamName);
		Assert.Contains("null element at index 0", exception.Message);
	}

	[TestMethod]
	public void FilterByWord_MultipleMatches_PreserveInputOrderAndItemIdentity()
	{
		IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord(s_items, "a");

		Assert.HasCount(3, result);
		Assert.AreSame(s_items[0], result[0]);
		Assert.AreSame(s_items[1], result[1]);
		Assert.AreSame(s_items[2], result[2]);
	}

	[TestMethod]
	public void FilterByWord_WhitespaceOnlyWord_MatchesFilterTextLiterally()
	{
		// A whitespace-only word is matched literally rather than treated as empty; only an item whose
		// filter text contains the whitespace matches.
		var item = new CompletionItem("display") { FilterText = "Beta: " };

		IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord([item], " ");

		Assert.HasCount(1, result);
	}

	[TestMethod]
	public void FilterByWord_EmptyWord_ReturnsCallerOwnedCopyOfAllItems()
	{
		IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord(s_items, string.Empty);

		Assert.AreNotSame(s_items, result);
		Assert.AreSequenceEqual([.. s_items], [.. result]);
	}

	[TestMethod]
	public void FilterByWord_UnderTurkishCulture_KeepsOrdinalCaseInsensitiveMatching()
	{
		// Turkish maps 'I' and 'i' to different letters; the ordinal contract must keep ASCII casing
		// variants matching regardless of the current culture.
		CultureInfo previousCulture = CultureInfo.CurrentCulture;
		CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
			CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

			IReadOnlyList<CompletionItem> items = [new CompletionItem("Item") { InsertText = "Item" }];

			IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord(items, "item");

			Assert.HasCount(1, result);
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
		IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord(s_items, "ET");

		Assert.HasCount(1, result);
		Assert.AreEqual("Beta: ", result[0].InsertText);
	}

	[TestMethod]
	public void FilterByWord_NonAsciiCaseVariant_MatchesUnderOrdinalFolding()
	{
		// Ordinal case-insensitivity folds through the invariant casing table, so a non-ASCII
		// character keeps matching its case variant instead of depending on the current culture.
		var item = new CompletionItem("display") { FilterText = "Caf\u00E9" };

		IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord([item], "CAF\u00C9");

		Assert.HasCount(1, result);
		Assert.AreSame(item, result[0]);
	}

	[TestMethod]
	public void FilterByWord_NoMatch_ReturnsAndReusesTheSharedEmptyResult()
	{
		// The no-match refresh path returns one shared empty list instead of allocating per call.
		IReadOnlyList<CompletionItem> first = CompletionFilter.FilterByWord(s_items, "Zzz");
		IReadOnlyList<CompletionItem> second = CompletionFilter.FilterByWord(s_items, "Zzz");

		Assert.IsEmpty(first);
		Assert.AreSame(first, second);
	}

	[TestMethod]
	public void FilterByWord_MatchesFilterTextWhenInsertTextDiffers()
	{
		var item = new CompletionItem("display") { InsertText = "inserted", FilterText = "aliased" };

		IReadOnlyList<CompletionItem> result = CompletionFilter.FilterByWord([item], "alias");

		Assert.HasCount(1, result);
	}

	[TestMethod]
	public void FilterByWord_DoesNotMatchInsertText()
	{
		// The match rule covers filter text and its label fallback only; commit text must not widen it.
		var aliased = new CompletionItem("display") { InsertText = "inserted", FilterText = "aliased" };

		Assert.IsEmpty(CompletionFilter.FilterByWord([aliased], "sert"));

		// The catalog's Beta item inserts "Beta: " while its label is "Beta"; a word that only the
		// insertion text contains selects nothing either.
		Assert.IsEmpty(CompletionFilter.FilterByWord(s_items, "a: "));
	}
}
