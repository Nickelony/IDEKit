using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DocumentSymbolTests
{
	[TestMethod]
	public void Constructor_WithoutRanges_LeavesRangesNull()
	{
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Variable);

		Assert.AreEqual("name", symbol.Name);
		Assert.IsNull(symbol.SelectionRange);
		Assert.IsNull(symbol.Range);
		Assert.IsEmpty(symbol.Children);
	}

	[TestMethod]
	public void Constructor_SelectionRange_SetsFullRangeWhenOmitted()
	{
		var selection = new TextRange(4, 5);
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Method, selectionRange: selection);

		Assert.AreEqual(selection, symbol.SelectionRange);
		Assert.AreEqual(selection, symbol.Range);
	}

	[TestMethod]
	public void Constructor_FullRangeOverridesSelectionRange()
	{
		var selection = new TextRange(4, 5);
		var range = new TextRange(0, 20);
		var symbol = new DocumentSymbol(name: "name", kind: DocumentSymbolKind.Method, range, selectionRange: selection);

		Assert.AreEqual(selection, symbol.SelectionRange);
		Assert.AreEqual(range, symbol.Range);
	}

	[TestMethod]
	public void Constructor_RangeOnly_LeavesSelectionRangeNull()
	{
		var range = new TextRange(0, 20);
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Method, range);

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
		var symbol = new DocumentSymbol(name: "name", kind: DocumentSymbolKind.Method, range, selectionRange: selection);

		Assert.AreEqual(range, symbol.Range);
		Assert.AreEqual(selection, symbol.SelectionRange);
	}

	[TestMethod]
	[DataRow(null, DisplayName = "Null")]
	[DataRow("   ", DisplayName = "Blank")]
	public void Initializer_NullOrBlankDetail_IsNull(string? detail)
	{
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Class) { Detail = detail };
		Assert.IsNull(symbol.Detail);
	}

	[TestMethod]
	public void Initializer_Detail_IsTrimmed()
	{
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Class) { Detail = "  int  " };
		Assert.AreEqual("int", symbol.Detail);
	}

	[TestMethod]
	public void Initializer_Children_ArePreserved()
	{
		var child = new DocumentSymbol("child", DocumentSymbolKind.Field);
		var symbol = new DocumentSymbol("parent", DocumentSymbolKind.Class) { Children = [child] };

		Assert.HasCount(1, symbol.Children);
		Assert.AreSame(child, symbol.Children[0]);
	}

	[TestMethod]
	public void Initializer_NullChildren_MeansNoChildren()
	{
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Class) { Children = null! };
		Assert.IsEmpty(symbol.Children);
	}

	[TestMethod]
	public void Initializer_EmptyChildrenList_MeansNoChildren()
	{
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Class) { Children = [] };
		Assert.IsEmpty(symbol.Children);
	}

	[TestMethod]
	public void Initializer_Children_AreAnOwnedSnapshot()
	{
		var child = new DocumentSymbol("child", DocumentSymbolKind.Field);
		var children = new List<DocumentSymbol> { child };

		var symbol = new DocumentSymbol("parent", DocumentSymbolKind.Class) { Children = children };

		children.Clear();

		Assert.HasCount(1, symbol.Children);
		Assert.AreSame(child, symbol.Children[0]);
	}

	[TestMethod]
	public void Initializer_Data_IsPreserved()
	{
		object payload = new();
		var symbol = new DocumentSymbol("name", DocumentSymbolKind.Variable) { Data = payload };

		Assert.AreSame(payload, symbol.Data);
	}

	[TestMethod]
	public void Initializer_ChildrenWithNullElement_Throws()
	{
		List<DocumentSymbol> children = [new DocumentSymbol("child", DocumentSymbolKind.Field), null!];

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			new DocumentSymbol("parent", DocumentSymbolKind.Class) { Children = children }
		);

		Assert.AreEqual(nameof(DocumentSymbol.Children), exception.ParamName);
		Assert.Contains("index 1", exception.Message, "The error should identify the offending element.");
	}

	[TestMethod]
	public void Constructor_UndefinedKind_Throws() => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DocumentSymbol("name", default));

	[TestMethod]
	public void Constructor_BlankName_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new DocumentSymbol("   ", DocumentSymbolKind.Variable));
}
