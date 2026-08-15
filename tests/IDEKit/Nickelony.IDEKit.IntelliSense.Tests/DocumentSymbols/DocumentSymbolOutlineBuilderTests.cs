using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.IDEKit.IntelliSense.Tests.DocumentSymbols;

[TestClass]
public sealed class DocumentSymbolOutlineBuilderTests
{
	private sealed record Node(string Text, string Payload);

	[TestMethod]
	public void BuildFlatOutline_ProducesOneSymbolPerItemWithSelectedKindAndData()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a"), new Node("Beta", "b")];
		var projection = new DocumentSymbolProjection<Node>(
			node => node.Text,
			_ => TextDocumentSymbolKind.Function,
			node => node.Payload);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildFlatOutline(nodes, projection);

		Assert.AreEqual(2, symbols.Count);
		Assert.AreEqual("Alpha", symbols[0].Name);
		Assert.AreEqual(TextDocumentSymbolKind.Function, symbols[0].Kind);
		Assert.AreEqual("a", symbols[0].Data);
		Assert.AreEqual(0, symbols[0].Children.Count);
		Assert.AreEqual("Beta", symbols[1].Name);
		Assert.AreEqual("b", symbols[1].Data);
	}

	[TestMethod]
	public void BuildFlatOutline_WithoutDataSelector_KeepsDataNull()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a")];
		var projection = new DocumentSymbolProjection<Node>(node => node.Text, _ => TextDocumentSymbolKind.Variable);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildFlatOutline(nodes, projection);

		Assert.AreEqual(1, symbols.Count);
		Assert.IsNull(symbols[0].Data);
	}

	[TestMethod]
	public void BuildFlatOutline_WithRangeAndDetailSelectors_ProjectsRangesAndDetail()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a")];
		var range = new TextRange(0, 10);
		var selectionRange = new TextRange(2, 5);
		var projection = new DocumentSymbolProjection<Node>(
			node => node.Text,
			_ => TextDocumentSymbolKind.Function,
			dataSelector: node => node.Payload,
			rangeSelector: _ => range,
			selectionRangeSelector: _ => selectionRange,
			detailSelector: _ => "  detail  ");

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildFlatOutline(nodes, projection);

		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual(range, symbols[0].Range);
		Assert.AreEqual(selectionRange, symbols[0].SelectionRange);
		Assert.AreEqual("detail", symbols[0].Detail);
	}

	[TestMethod]
	public void BuildFlatOutline_WithoutOptionalSelectors_LeavesRangesAndDetailUnset()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a")];
		var projection = new DocumentSymbolProjection<Node>(node => node.Text, _ => TextDocumentSymbolKind.Function);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildFlatOutline(nodes, projection);

		Assert.IsNull(symbols[0].Range);
		Assert.IsNull(symbols[0].SelectionRange);
		Assert.IsNull(symbols[0].Detail);
	}

	[TestMethod]
	public void BuildGroupedOutline_WithRangeAndDetailSelectors_ProjectsGroupAndChildValues()
	{
		IReadOnlyList<string> groups = ["Group"];
		var groupRange = new TextRange(0, 20);
		var itemRange = new TextRange(2, 3);
		var groupProjection = new DocumentSymbolProjection<string>(
			group => group,
			_ => TextDocumentSymbolKind.Module,
			rangeSelector: _ => groupRange);
		var itemProjection = new DocumentSymbolProjection<string>(
			text => text,
			_ => TextDocumentSymbolKind.Variable,
			rangeSelector: _ => itemRange,
			detailSelector: _ => "detail");

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildGroupedOutline(
			groups,
			groupProjection,
			_ => ["Child"],
			itemProjection);

		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual(groupRange, symbols[0].Range);
		Assert.AreEqual(1, symbols[0].Children.Count);
		Assert.AreEqual(itemRange, symbols[0].Children[0].Range);
		Assert.AreEqual("detail", symbols[0].Children[0].Detail);
	}

	[TestMethod]
	public void BuildFlatOutline_NullProjection_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => DocumentSymbolOutlineBuilder.BuildFlatOutline<string>(["value"], null!));

		Assert.AreEqual("projection", exception.ParamName);
	}

	[TestMethod]
	public void BuildFlatOutline_NullItems_Throws()
	{
		var projection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => DocumentSymbolOutlineBuilder.BuildFlatOutline<string>(null!, projection));

		Assert.AreEqual("items", exception.ParamName);
	}

	[TestMethod]
	public void BuildFlatOutline_EmptyItems_ReturnsEmptyList()
	{
		var projection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildFlatOutline<string>([], projection);

		Assert.AreEqual(0, symbols.Count);
	}

	[TestMethod]
	public void BuildGroupedOutline_NullGroups_Throws()
	{
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => DocumentSymbolOutlineBuilder.BuildGroupedOutline<string, string>(null!, groupProjection, _ => [], itemProjection));

		Assert.AreEqual("groups", exception.ParamName);
	}

	[TestMethod]
	public void BuildGroupedOutline_NullItemsSelector_Throws()
	{
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => DocumentSymbolOutlineBuilder.BuildGroupedOutline<string, string>(["Group"], groupProjection, null!, itemProjection));

		Assert.AreEqual("groupItemsSelector", exception.ParamName);
	}

	[TestMethod]
	public void BuildGroupedOutline_NullItemProjection_Throws()
	{
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => DocumentSymbolOutlineBuilder.BuildGroupedOutline<string, string>(["Group"], groupProjection, _ => [], null!));

		Assert.AreEqual("itemProjection", exception.ParamName);
	}

	[TestMethod]
	public void BuildGroupedOutline_EmptyGroups_ReturnsEmptyList()
	{
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildGroupedOutline<string, string>([], groupProjection, _ => [], itemProjection);

		Assert.AreEqual(0, symbols.Count);
	}

	[TestMethod]
	public void BuildGroupedOutline_ProducesRootPerGroupWithChildren()
	{
		IReadOnlyList<string> groups = ["First", "Second"];
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<Node>(
			node => node.Text,
			_ => TextDocumentSymbolKind.Variable,
			node => node.Payload);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildGroupedOutline(
			groups,
			groupProjection,
			SelectGroupNodes,
			itemProjection);

		Assert.AreEqual(2, symbols.Count);

		Assert.AreEqual("First", symbols[0].Name);
		Assert.AreEqual(TextDocumentSymbolKind.Module, symbols[0].Kind);
		Assert.AreEqual(2, symbols[0].Children.Count);
		Assert.AreEqual("A", symbols[0].Children[0].Name);
		Assert.AreEqual(TextDocumentSymbolKind.Variable, symbols[0].Children[0].Kind);
		Assert.AreEqual("a", symbols[0].Children[0].Data);

		Assert.AreEqual("Second", symbols[1].Name);
		Assert.AreEqual(1, symbols[1].Children.Count);
		Assert.AreEqual("C", symbols[1].Children[0].Name);
	}

	[TestMethod]
	public void BuildGroupedOutline_GroupDataSelector_StoresGroupPayload()
	{
		IReadOnlyList<string> groups = ["First"];
		var groupProjection = new DocumentSymbolProjection<string>(
			group => group,
			_ => TextDocumentSymbolKind.Module,
			group => group.Length);
		var itemProjection = new DocumentSymbolProjection<Node>(node => node.Text, _ => TextDocumentSymbolKind.Variable);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildGroupedOutline(
			groups,
			groupProjection,
			_ => [],
			itemProjection);

		Assert.AreEqual(5, symbols[0].Data);
	}

	[TestMethod]
	public void BuildGroupedOutline_EmptyGroup_ProducesRootWithoutChildren()
	{
		IReadOnlyList<string> groups = ["Empty"];
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolOutlineBuilder.BuildGroupedOutline(
			groups,
			groupProjection,
			_ => [],
			itemProjection);

		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual("Empty", symbols[0].Name);
		Assert.AreEqual(0, symbols[0].Children.Count);
	}

	[TestMethod]
	public void BuildGroupedOutline_NullItemList_Throws()
	{
		IReadOnlyList<string> groups = ["Group", "NullGroup"];
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => DocumentSymbolOutlineBuilder.BuildGroupedOutline(
				groups,
				groupProjection,
				group => group == "Group" ? new[] { "value" } : null!,
				itemProjection));

		StringAssert.Contains(exception.Message, "items selector", "The error should name the selector.");
		StringAssert.Contains(exception.Message, "index 1", "The error should identify the offending group.");
	}

	[TestMethod]
	public void BuildFlatOutline_NullNameSelectorResult_Throws()
	{
		var projection = new DocumentSymbolProjection<string>(_ => null!, _ => TextDocumentSymbolKind.Variable);

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => DocumentSymbolOutlineBuilder.BuildFlatOutline(["value"], projection));

		StringAssert.Contains(exception.Message, "name selector", "The error should name the selector.");
		StringAssert.Contains(exception.Message, "index 0", "The error should identify the offending item.");
	}

	[TestMethod]
	public void BuildGroupedOutline_NullGroupNameSelectorResult_Throws()
	{
		var groupProjection = new DocumentSymbolProjection<string>(_ => null!, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => DocumentSymbolOutlineBuilder.BuildGroupedOutline(["Group"], groupProjection, _ => [], itemProjection));

		StringAssert.Contains(exception.Message, "group at index 0", "The error should identify the offending group.");
	}

	[TestMethod]
	public void BuildGroupedOutline_NullItemNameSelectorResult_Throws()
	{
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(
			text => text == "bad" ? null! : text, _ => TextDocumentSymbolKind.Variable);

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => DocumentSymbolOutlineBuilder.BuildGroupedOutline(
				["First", "Second"],
				groupProjection,
				group => group == "First" ? new[] { "ok", "bad" } : new[] { "ok2" },
				itemProjection));

		StringAssert.Contains(exception.Message, "an item at index 1", "The error should identify the offending item.");
		StringAssert.Contains(exception.Message, "group at index 0", "The error should identify the group that contains the offending item.");
	}

	[TestMethod]
	public void BuildFlatOutline_UndefinedKindSelectorResult_ThrowsArgumentOutOfRangeException()
	{
		// The builder does not re-validate the projection, so the kind check of TextDocumentSymbol
		// propagates: a selector that returns default(TextDocumentSymbolKind) is rejected.
		var projection = new DocumentSymbolProjection<string>(text => text, _ => default(TextDocumentSymbolKind));

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => DocumentSymbolOutlineBuilder.BuildFlatOutline(["value"], projection));
	}

	[TestMethod]
	public void BuildGroupedOutline_UndefinedKindSelectorResult_ThrowsArgumentOutOfRangeException()
	{
		var groupProjection = new DocumentSymbolProjection<string>(group => group, _ => TextDocumentSymbolKind.Module);
		var itemProjection = new DocumentSymbolProjection<string>(text => text, _ => default(TextDocumentSymbolKind));

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => DocumentSymbolOutlineBuilder.BuildGroupedOutline(["Group"], groupProjection, _ => ["value"], itemProjection));
	}

	private static IReadOnlyList<Node> SelectGroupNodes(string group)
	{
		return group == "First"
			? [new Node("A", "a"), new Node("B", "b")]
			: [new Node("C", "c")];
	}
}
