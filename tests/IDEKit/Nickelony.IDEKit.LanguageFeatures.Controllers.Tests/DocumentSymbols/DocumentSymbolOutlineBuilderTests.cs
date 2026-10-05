using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class DocumentSymbolOutlineBuilderTests
{
	private sealed record Node(string Text, string Payload);

	[TestMethod]
	public void Build_Flat_ProducesOneSymbolPerItemWithSelectedKindAndData()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a"), new Node("Beta", "b")];

		var projection = new DocumentSymbolOutlineHooks<Node>
		{
			NameSelector = node => node.Text,
			KindSelector = _ => DocumentSymbolKind.Function,
			DataSelector = node => node.Payload,
		};

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(nodes, projection);

		Assert.HasCount(2, symbols);
		Assert.AreEqual("Alpha", symbols[0].Name);
		Assert.AreEqual(DocumentSymbolKind.Function, symbols[0].Kind);
		Assert.AreEqual("a", symbols[0].Data);
		Assert.IsEmpty(symbols[0].Children);
		Assert.AreEqual("Beta", symbols[1].Name);
		Assert.AreEqual("b", symbols[1].Data);
	}

	[TestMethod]
	public void Build_Flat_WithoutDataSelector_KeepsDataNull()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a")];
		var projection = new DocumentSymbolOutlineHooks<Node> { NameSelector = node => node.Text, KindSelector = _ => DocumentSymbolKind.Variable };

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(nodes, projection);

		Assert.HasCount(1, symbols);
		Assert.IsNull(symbols[0].Data);
	}

	[TestMethod]
	public void Build_Flat_WithRangeAndDetailSelectors_ProjectsRangesAndDetail()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a")];

		var range = new TextRange(0, 10);
		var selectionRange = new TextRange(2, 5);

		var projection = new DocumentSymbolOutlineHooks<Node>
		{
			NameSelector = node => node.Text,
			KindSelector = _ => DocumentSymbolKind.Function,
			DataSelector = node => node.Payload,
			RangeSelector = _ => range,
			SelectionRangeSelector = _ => selectionRange,
			DetailSelector = _ => " detail ",
		};

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(nodes, projection);

		Assert.HasCount(1, symbols);
		Assert.AreEqual(range, symbols[0].Range);
		Assert.AreEqual(selectionRange, symbols[0].SelectionRange);
		Assert.AreEqual("detail", symbols[0].Detail);
	}

	[TestMethod]
	public void Build_Flat_WithoutOptionalSelectors_LeavesRangesAndDetailUnset()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a")];
		var projection = new DocumentSymbolOutlineHooks<Node> { NameSelector = node => node.Text, KindSelector = _ => DocumentSymbolKind.Function };

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(nodes, projection);

		Assert.IsNull(symbols[0].Range);
		Assert.IsNull(symbols[0].SelectionRange);
		Assert.IsNull(symbols[0].Detail);
	}

	[TestMethod]
	public void Build_Grouped_WithRangeAndDetailSelectors_ProjectsGroupAndChildValues()
	{
		IReadOnlyList<string> groups = ["Group"];

		var groupRange = new TextRange(0, 20);
		var itemRange = new TextRange(2, 3);

		var groupProjection = new DocumentSymbolOutlineHooks<string>
		{
			NameSelector = group => group,
			KindSelector = _ => DocumentSymbolKind.Module,
			RangeSelector = _ => groupRange,
		};

		var itemProjection = new DocumentSymbolOutlineHooks<string>
		{
			NameSelector = text => text,
			KindSelector = _ => DocumentSymbolKind.Variable,
			RangeSelector = _ => itemRange,
			DetailSelector = _ => "detail",
		};

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(
			groups,
			groupHooks: groupProjection,
			groupItemsSelector: _ => ["Child"],
			itemHooks: itemProjection
		);

		Assert.HasCount(1, symbols);
		Assert.AreEqual(groupRange, symbols[0].Range);
		Assert.HasCount(1, symbols[0].Children);
		Assert.AreEqual(itemRange, symbols[0].Children[0].Range);
		Assert.AreEqual("detail", symbols[0].Children[0].Detail);
	}

	[TestMethod]
	public void Build_Flat_NullHooks_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentSymbolOutlineBuilder.Build<string>(["value"], null!)
		);

		Assert.AreEqual("hooks", exception.ParamName);
	}

	[TestMethod]
	public void Build_Flat_NullItems_Throws()
	{
		var projection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentSymbolOutlineBuilder.Build<string>(null!, projection)
		);

		Assert.AreEqual("items", exception.ParamName);
	}

	[TestMethod]
	public void Build_Flat_EmptyItems_ReturnsEmptyList()
	{
		var projection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build<string>([], projection);

		Assert.IsEmpty(symbols);
	}

	[TestMethod]
	public void Build_Grouped_NullGroups_Throws()
	{
		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };
		var itemProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentSymbolOutlineBuilder.Build<string, string>(
				groups: null!,
				groupHooks: groupProjection,
				groupItemsSelector: _ => [],
				itemHooks: itemProjection
			)
		);

		Assert.AreEqual("groups", exception.ParamName);
	}

	[TestMethod]
	public void Build_Grouped_NullItemsSelector_Throws()
	{
		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };
		var itemProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentSymbolOutlineBuilder.Build<string, string>(
				groups: ["Group"],
				groupHooks: groupProjection,
				groupItemsSelector: null!,
				itemHooks: itemProjection
			)
		);

		Assert.AreEqual("groupItemsSelector", exception.ParamName);
	}

	[TestMethod]
	public void Build_Grouped_NullItemHooks_Throws()
	{
		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentSymbolOutlineBuilder.Build<string, string>(
				groups: ["Group"],
				groupHooks: groupProjection,
				groupItemsSelector: _ => [],
				itemHooks: null!
			)
		);

		Assert.AreEqual("itemHooks", exception.ParamName);
	}

	[TestMethod]
	public void Build_Grouped_EmptyGroups_ReturnsEmptyList()
	{
		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };
		var itemProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build<string, string>(
			groups: [],
			groupHooks: groupProjection,
			groupItemsSelector: _ => [],
			itemHooks: itemProjection
		);

		Assert.IsEmpty(symbols);
	}

	[TestMethod]
	public void Build_Grouped_ProducesRootPerGroupWithChildren()
	{
		IReadOnlyList<string> groups = ["First", "Second"];

		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };

		var itemProjection = new DocumentSymbolOutlineHooks<Node>
		{
			NameSelector = node => node.Text,
			KindSelector = _ => DocumentSymbolKind.Variable,
			DataSelector = node => node.Payload,
		};

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(
			groups,
			groupHooks: groupProjection,
			groupItemsSelector: SelectGroupNodes,
			itemHooks: itemProjection
		);

		Assert.HasCount(2, symbols);

		Assert.AreEqual("First", symbols[0].Name);
		Assert.AreEqual(DocumentSymbolKind.Module, symbols[0].Kind);
		Assert.HasCount(2, symbols[0].Children);
		Assert.AreEqual("A", symbols[0].Children[0].Name);
		Assert.AreEqual(DocumentSymbolKind.Variable, symbols[0].Children[0].Kind);
		Assert.AreEqual("a", symbols[0].Children[0].Data);

		Assert.AreEqual("Second", symbols[1].Name);
		Assert.HasCount(1, symbols[1].Children);
		Assert.AreEqual("C", symbols[1].Children[0].Name);
	}

	[TestMethod]
	public void Build_Grouped_GroupDataSelector_StoresGroupPayload()
	{
		IReadOnlyList<string> groups = ["First"];

		var groupProjection = new DocumentSymbolOutlineHooks<string>
		{
			NameSelector = group => group,
			KindSelector = _ => DocumentSymbolKind.Module,
			DataSelector = group => group.Length,
		};

		var itemProjection = new DocumentSymbolOutlineHooks<Node>
		{
			NameSelector = node => node.Text,
			KindSelector = _ => DocumentSymbolKind.Variable,
		};

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(
			groups,
			groupHooks: groupProjection,
			groupItemsSelector: _ => [],
			itemHooks: itemProjection
		);

		Assert.AreEqual(5, symbols[0].Data);
	}

	[TestMethod]
	public void Build_Grouped_EmptyGroup_ProducesRootWithoutChildren()
	{
		IReadOnlyList<string> groups = ["Empty"];

		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };
		var itemProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolOutlineBuilder.Build(
			groups,
			groupHooks: groupProjection,
			groupItemsSelector: _ => [],
			itemHooks: itemProjection
		);

		Assert.HasCount(1, symbols);
		Assert.AreEqual("Empty", symbols[0].Name);
		Assert.IsEmpty(symbols[0].Children);
	}

	[TestMethod]
	public void Build_Grouped_NullItemList_Throws()
	{
		IReadOnlyList<string> groups = ["Group", "NullGroup"];

		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };
		var itemProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
			DocumentSymbolOutlineBuilder.Build(
				groups,
				groupHooks: groupProjection,
				groupItemsSelector: group => group == "Group" ? new[] { "value" } : null!,
				itemHooks: itemProjection
			)
		);

		Assert.Contains("items selector", exception.Message, "The error should name the selector.");
		Assert.Contains("index 1", exception.Message, "The error should identify the offending group.");
	}

	[TestMethod]
	public void Build_Flat_NullNameSelectorResult_Throws()
	{
		var projection = new DocumentSymbolOutlineHooks<string> { NameSelector = _ => null!, KindSelector = _ => DocumentSymbolKind.Variable };

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
			DocumentSymbolOutlineBuilder.Build(["value"], projection)
		);

		Assert.Contains("name selector", exception.Message, "The error should name the selector.");
		Assert.Contains("index 0", exception.Message, "The error should identify the offending item.");
	}

	[TestMethod]
	public void Build_Grouped_NullGroupNameSelectorResult_Throws()
	{
		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = _ => null!, KindSelector = _ => DocumentSymbolKind.Module };
		var itemProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => DocumentSymbolKind.Variable };

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
			DocumentSymbolOutlineBuilder.Build(groups: ["Group"], groupHooks: groupProjection, groupItemsSelector: _ => [], itemHooks: itemProjection)
		);

		Assert.Contains("group at index 0", exception.Message, "The error should identify the offending group.");
	}

	[TestMethod]
	public void Build_Grouped_NullItemNameSelectorResult_Throws()
	{
		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };

		var itemProjection = new DocumentSymbolOutlineHooks<string>
		{
			NameSelector = text => text == "bad" ? null! : text,
			KindSelector = _ => DocumentSymbolKind.Variable,
		};

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
			DocumentSymbolOutlineBuilder.Build(
				groups: ["First", "Second"],
				groupHooks: groupProjection,
				groupItemsSelector: group => group == "First" ? new[] { "ok", "bad" } : ["ok2"],
				itemHooks: itemProjection
			)
		);

		Assert.Contains("an item at index 1", exception.Message, "The error should identify the offending item.");
		Assert.Contains("group at index 0", exception.Message, "The error should identify the group that contains the offending item.");
	}

	[TestMethod]
	public void Build_Flat_UndefinedKindSelectorResult_ThrowsArgumentOutOfRangeException()
	{
		// The builder does not re-validate the projection, so the kind check of DocumentSymbol
		// propagates: a selector that returns default(DocumentSymbolKind) is rejected.
		var projection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => default };
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DocumentSymbolOutlineBuilder.Build(["value"], projection));
	}

	[TestMethod]
	public void Build_Grouped_UndefinedKindSelectorResult_ThrowsArgumentOutOfRangeException()
	{
		var groupProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = group => group, KindSelector = _ => DocumentSymbolKind.Module };
		var itemProjection = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = _ => default };

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			DocumentSymbolOutlineBuilder.Build(
				groups: ["Group"],
				groupHooks: groupProjection,
				groupItemsSelector: _ => ["value"],
				itemHooks: itemProjection
			)
		);
	}

	private static IReadOnlyList<Node> SelectGroupNodes(string group) =>
		group == "First" ? [new Node("A", "a"), new Node("B", "b")] : [new Node("C", "c")];
}
