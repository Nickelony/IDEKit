using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed partial class CompletionItemTests
{
	[TestMethod]
	public void InsertText_NotInitialized_UsesLabel()
	{
		var item = new CompletionItem("label");
		Assert.AreEqual("label", item.InsertText);
	}

	[TestMethod]
	public void InsertText_ExplicitNull_UsesLabel()
	{
		var item = new CompletionItem("label") { InsertText = null };
		Assert.AreEqual("label", item.InsertText);
	}

	[TestMethod]
	public void InsertText_Whitespace_IsPreserved()
	{
		// Only null falls back to the label; a whitespace-only insertion is a valid commit text.
		var item = new CompletionItem("label") { InsertText = "  " };
		Assert.AreEqual("  ", item.InsertText);
	}

	[TestMethod]
	public void InsertText_Explicit_IsPreserved()
	{
		var item = new CompletionItem("label") { InsertText = "inserted" };
		Assert.AreEqual("inserted", item.InsertText);
	}

	[TestMethod]
	public void Documentation_Blank_IsNull()
	{
		var item = new CompletionItem("label") { Documentation = "   " };
		Assert.IsNull(item.Documentation);
	}

	[TestMethod]
	public void Documentation_Padded_IsTrimmed()
	{
		var item = new CompletionItem("label") { Documentation = "  text  " };
		Assert.AreEqual("text", item.Documentation?.Value);
	}

	[TestMethod]
	public void Documentation_Markdown_IsNotTrimmed()
	{
		var item = new CompletionItem("label") { Documentation = new MarkupContent("  **text**  ", MarkupKind.Markdown) };

		Assert.AreEqual("  **text**  ", item.Documentation?.Value);
		Assert.AreEqual(MarkupKind.Markdown, item.Documentation?.Kind);
	}

	[TestMethod]
	public void Kind_NotInitialized_DefaultsToGeneric()
	{
		var item = new CompletionItem("label");
		Assert.AreSame(CompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void Kind_Explicit_IsPreserved()
	{
		var item = new CompletionItem("label") { Kind = CompletionItemKind.Method };
		Assert.AreSame(CompletionItemKind.Method, item.Kind);
	}

	[TestMethod]
	public void Kind_Null_DefaultsToGeneric()
	{
		var item = new CompletionItem("label") { Kind = null! };
		Assert.AreSame(CompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void Priority_NonFiniteValue_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionItem("label") { Priority = double.NaN });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionItem("label") { Priority = double.PositiveInfinity });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionItem("label") { Priority = double.NegativeInfinity });
	}

	[TestMethod]
	public void Priority_FiniteValue_IsStored()
	{
		var item = new CompletionItem("label") { Priority = -2.5 };
		Assert.AreEqual(-2.5, item.Priority);
	}

	[TestMethod]
	public void SortText_NotInitialized_IsNull()
	{
		var item = new CompletionItem("label");
		Assert.IsNull(item.SortText);
	}

	[TestMethod]
	public void SortText_Blank_IsNull()
	{
		var item = new CompletionItem("label") { SortText = "   " };
		Assert.IsNull(item.SortText);
	}

	[TestMethod]
	public void SortText_Explicit_IsPreservedVerbatim()
	{
		// Ordering is a lexicographic comparison, so a non-blank value is not trimmed.
		var item = new CompletionItem("label") { SortText = " 0002" };
		Assert.AreEqual(" 0002", item.SortText);
	}

	[TestMethod]
	public void IsPreselected_NotInitialized_IsFalse()
	{
		var item = new CompletionItem("label");
		Assert.IsFalse(item.IsPreselected);
	}

	[TestMethod]
	public void IsPreselected_Explicit_IsPreserved()
	{
		var item = new CompletionItem("label") { IsPreselected = true };
		Assert.IsTrue(item.IsPreselected);
	}

	[TestMethod]
	public void FilterText_NotInitialized_UsesLabel()
	{
		var item = new CompletionItem("label");
		Assert.AreEqual("label", item.FilterText);
	}

	[TestMethod]
	public void FilterText_BlankLabel_IsUsedAsTheFallbackUnchanged()
	{
		// Neither the label fallback nor the blank-filter normalization trims the result: an item with
		// a blank label and a blank filter text reports the blank label.
		var item = new CompletionItem("  ") { FilterText = "  " };
		Assert.AreEqual("  ", item.FilterText);
	}

	[TestMethod]
	public void FilterText_Whitespace_UsesLabel()
	{
		var item = new CompletionItem("label") { FilterText = "   " };
		Assert.AreEqual("label", item.FilterText);
	}

	[TestMethod]
	public void FilterText_Explicit_IsPreserved()
	{
		var item = new CompletionItem("label") { FilterText = "filtered" };
		Assert.AreEqual("filtered", item.FilterText);
	}

	[TestMethod]
	[DataRow(null, DisplayName = "Null")]
	[DataRow("   ", DisplayName = "Blank")]
	public void Detail_NullOrBlank_IsNull(string? detail)
	{
		var item = new CompletionItem("label") { Detail = detail };
		Assert.IsNull(item.Detail);
	}

	[TestMethod]
	public void Detail_Padded_IsTrimmed()
	{
		var item = new CompletionItem("label") { Detail = "  text  " };
		Assert.AreEqual("text", item.Detail);
	}

	[TestMethod]
	public void RequestMetadata_Initialized_IsPreserved()
	{
		var item = new CompletionItem("label")
		{
			RequestDocumentVersion = 3,
			RequestGeneration = 7,
			InsertTextFormat = CompletionInsertTextFormat.Snippet,
		};

		Assert.AreEqual(3, item.RequestDocumentVersion);
		Assert.AreEqual(7, item.RequestGeneration);
		Assert.AreEqual(CompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void InsertTextFormat_NotInitialized_DefaultsToPlainText()
	{
		var item = new CompletionItem("label");
		Assert.AreEqual(CompletionInsertTextFormat.PlainText, item.InsertTextFormat);
	}

	[TestMethod]
	public void InsertTextFormat_UndefinedValue_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionItem("label") { InsertTextFormat = (CompletionInsertTextFormat)42 });
}
