using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.IDEKit.IntelliSense.Tests.DocumentSymbols;

[TestClass]
public sealed class TextDocumentSymbolTests
{
	[TestMethod]
	public void Constructor_WithoutRanges_LeavesRangesNull()
	{
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Variable);

		Assert.AreEqual("name", symbol.Name);
		Assert.IsNull(symbol.SelectionRange);
		Assert.IsNull(symbol.Range);
		Assert.AreEqual(0, symbol.Children.Count);
	}

	[TestMethod]
	public void Constructor_SelectionRange_SetsFullRangeWhenOmitted()
	{
		var selection = new TextRange(4, 5);
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Method, selectionRange: selection);

		Assert.AreEqual(selection, symbol.SelectionRange);
		Assert.AreEqual(selection, symbol.Range);
	}

	[TestMethod]
	public void Constructor_FullRangeOverridesSelectionRange()
	{
		var selection = new TextRange(4, 5);
		var range = new TextRange(0, 20);
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Method, range, selection);

		Assert.AreEqual(selection, symbol.SelectionRange);
		Assert.AreEqual(range, symbol.Range);
	}

	[TestMethod]
	public void Constructor_RangeOnly_LeavesSelectionRangeNull()
	{
		var range = new TextRange(0, 20);
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Method, range);

		Assert.IsNull(symbol.SelectionRange);
		Assert.AreEqual(range, symbol.Range);
	}

	[TestMethod]
	public void Constructor_SelectionRangeOutsideFullRange_IsStoredWithoutContainmentValidation()
	{
		// Providers may supply partial range data, so the library stores the ranges as supplied
		// instead of validating that the selection range is contained in the full range.
		var range = new TextRange(0, 5);
		var selection = new TextRange(10, 3);
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Method, range, selection);

		Assert.AreEqual(range, symbol.Range);
		Assert.AreEqual(selection, symbol.SelectionRange);
	}

	[TestMethod]
	[DataRow(null, DisplayName = "Null")]
	[DataRow("   ", DisplayName = "Blank")]
	public void Initializer_NullOrBlankDetail_IsNull(string? detail)
	{
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Class) { Detail = detail };

		Assert.IsNull(symbol.Detail);
	}

	[TestMethod]
	public void Initializer_Detail_IsTrimmed()
	{
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Class) { Detail = "  int  " };

		Assert.AreEqual("int", symbol.Detail);
	}

	[TestMethod]
	public void Initializer_Children_ArePreserved()
	{
		var child = new TextDocumentSymbol("child", TextDocumentSymbolKind.Field);
		var symbol = new TextDocumentSymbol("parent", TextDocumentSymbolKind.Class) { Children = [child] };

		Assert.AreEqual(1, symbol.Children.Count);
		Assert.AreSame(child, symbol.Children[0]);
	}

	[TestMethod]
	public void Initializer_NullChildren_MeansNoChildren()
	{
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Class) { Children = null! };

		Assert.AreEqual(0, symbol.Children.Count);
	}

	[TestMethod]
	public void Initializer_EmptyChildrenList_MeansNoChildren()
	{
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Class) { Children = [] };

		Assert.AreEqual(0, symbol.Children.Count);
	}

	[TestMethod]
	public void Initializer_Children_AreAnOwnedSnapshot()
	{
		var child = new TextDocumentSymbol("child", TextDocumentSymbolKind.Field);
		var children = new List<TextDocumentSymbol> { child };

		var symbol = new TextDocumentSymbol("parent", TextDocumentSymbolKind.Class) { Children = children };

		children.Clear();

		Assert.AreEqual(1, symbol.Children.Count);
		Assert.AreSame(child, symbol.Children[0]);
	}

	[TestMethod]
	public void Initializer_Data_IsPreserved()
	{
		var payload = new object();
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Variable) { Data = payload };

		Assert.AreSame(payload, symbol.Data);
	}

	[TestMethod]
	public void Initializer_ChildrenWithNullElement_Throws()
	{
		List<TextDocumentSymbol> children =
		[
			new TextDocumentSymbol("child", TextDocumentSymbolKind.Field),
			null!
		];

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
			() => new TextDocumentSymbol("parent", TextDocumentSymbolKind.Class) { Children = children });

		Assert.AreEqual(nameof(TextDocumentSymbol.Children), exception.ParamName);
		StringAssert.Contains(exception.Message, "index 1", "The error should identify the offending element.");
	}

	[TestMethod]
	public void Constructor_UndefinedKind_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new TextDocumentSymbol("name", default));

	[TestMethod]
	public void Constructor_BlankName_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(
			() => new TextDocumentSymbol("   ", TextDocumentSymbolKind.Variable));
}
