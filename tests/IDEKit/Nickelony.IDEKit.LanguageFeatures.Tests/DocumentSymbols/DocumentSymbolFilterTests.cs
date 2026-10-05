using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DocumentSymbolFilterTests
{
	[TestMethod]
	public void FilterByName_BlankFilter_ReturnsTheSuppliedList()
	{
		IReadOnlyList<DocumentSymbol> symbols = [new DocumentSymbol("alpha", DocumentSymbolKind.Function)];

		Assert.AreSame(symbols, DocumentSymbolFilter.FilterByName(symbols, string.Empty));
		Assert.AreSame(symbols, DocumentSymbolFilter.FilterByName(symbols, "   "));
	}

	[TestMethod]
	public void FilterByName_MatchesByOrdinalContainmentWithoutRegardToCase()
	{
		IReadOnlyList<DocumentSymbol> symbols =
		[
			new DocumentSymbol("SpawnObject", DocumentSymbolKind.Function),
			new DocumentSymbol("despawn", DocumentSymbolKind.Function),
			new DocumentSymbol("render", DocumentSymbolKind.Function),
		];

		IReadOnlyList<DocumentSymbol> result = DocumentSymbolFilter.FilterByName(symbols, "SPAWN");

		Assert.AreSequenceEqual(["SpawnObject", "despawn"], [.. result.Select(symbol => symbol.Name)]);
	}

	[TestMethod]
	public void FilterByName_MatchingDescendant_KeepsItsAncestorsAndDropsTheSiblingsOfTheMatch()
	{
		var matching = new DocumentSymbol("target", DocumentSymbolKind.Variable);
		var sibling = new DocumentSymbol("other", DocumentSymbolKind.Variable);
		var parent = new DocumentSymbol("module", DocumentSymbolKind.Module) { Children = [matching, sibling] };
		var unrelated = new DocumentSymbol("unrelated", DocumentSymbolKind.Function);

		IReadOnlyList<DocumentSymbol> result = DocumentSymbolFilter.FilterByName([parent, unrelated], "target");

		Assert.HasCount(1, result);
		Assert.AreEqual("module", result[0].Name);
		Assert.HasCount(1, result[0].Children);
		Assert.AreEqual("target", result[0].Children[0].Name);
	}

	[TestMethod]
	public void FilterByName_MatchingParent_KeepsOnlyTheChildrenThatStay()
	{
		var child = new DocumentSymbol("detail", DocumentSymbolKind.Variable);
		var parent = new DocumentSymbol("module", DocumentSymbolKind.Module) { Children = [child] };

		IReadOnlyList<DocumentSymbol> result = DocumentSymbolFilter.FilterByName([parent], "mod");

		Assert.HasCount(1, result);
		Assert.IsEmpty(result[0].Children);
	}

	[TestMethod]
	public void FilterByName_CopiesTheSymbolMembersTheFilterDoesNotChange()
	{
		object data = new();

		var range = new TextRange(2, 5);
		var selection = new TextRange(3, 2);

		var symbol = new DocumentSymbol(name: "alpha", kind: DocumentSymbolKind.Function, range, selectionRange: selection)
		{
			Detail = "function alpha()",
			Data = data,
		};

		DocumentSymbol copy = DocumentSymbolFilter.FilterByName([symbol], "alp").Single();

		Assert.AreNotSame(symbol, copy);
		Assert.AreEqual(DocumentSymbolKind.Function, copy.Kind);
		Assert.AreEqual(range, copy.Range);
		Assert.AreEqual(selection, copy.SelectionRange);
		Assert.AreEqual("function alpha()", copy.Detail);
		Assert.AreSame(data, copy.Data);
	}

	[TestMethod]
	public void FilterByName_NoMatch_ReturnsAnEmptyList()
	{
		IReadOnlyList<DocumentSymbol> result = DocumentSymbolFilter.FilterByName([new DocumentSymbol("alpha", DocumentSymbolKind.Function)], "zzz");
		Assert.IsEmpty(result);
	}

	[TestMethod]
	public void FilterByName_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolFilter.FilterByName(null!, "a"));
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolFilter.FilterByName([], null!));
	}
}
