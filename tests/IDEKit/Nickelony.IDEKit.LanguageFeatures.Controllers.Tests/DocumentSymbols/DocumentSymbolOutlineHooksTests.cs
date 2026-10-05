using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class DocumentSymbolOutlineHooksTests
{
	private static readonly DocumentSymbolOutlineHooks<string> s_valid = new()
	{
		NameSelector = text => text,
		KindSelector = _ => DocumentSymbolKind.Variable,
	};

	[TestMethod]
	public void Build_NullNameSelector_Throws()
	{
		var hooks = new DocumentSymbolOutlineHooks<string> { NameSelector = null!, KindSelector = _ => DocumentSymbolKind.Variable };

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => DocumentSymbolOutlineBuilder.Build(["a"], hooks));

		Assert.AreEqual("hooks", exception.ParamName);
	}

	[TestMethod]
	public void Build_NullKindSelector_Throws()
	{
		var hooks = new DocumentSymbolOutlineHooks<string> { NameSelector = text => text, KindSelector = null! };

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => DocumentSymbolOutlineBuilder.Build(["a"], hooks));

		Assert.AreEqual("hooks", exception.ParamName);
	}

	[TestMethod]
	public void BuildGrouped_HooksWithoutRequiredSelectors_ThrowNamingTheParameter()
	{
		var broken = new DocumentSymbolOutlineHooks<string> { NameSelector = null!, KindSelector = _ => DocumentSymbolKind.Variable };
		string[] groups = ["group"];

		ArgumentException group = Assert.ThrowsExactly<ArgumentException>(() =>
			DocumentSymbolOutlineBuilder.Build(groups, groupHooks: broken, groupItemsSelector: static _ => ["item"], itemHooks: s_valid)
		);

		ArgumentException item = Assert.ThrowsExactly<ArgumentException>(() =>
			DocumentSymbolOutlineBuilder.Build(groups, groupHooks: s_valid, groupItemsSelector: static _ => ["item"], itemHooks: broken)
		);

		Assert.AreEqual("groupHooks", group.ParamName);
		Assert.AreEqual("itemHooks", item.ParamName);
	}

	[TestMethod]
	public void WithoutOptionalSelectors_LeavesThemNull()
	{
		Assert.IsNull(s_valid.DataSelector);
		Assert.IsNull(s_valid.RangeSelector);
		Assert.IsNull(s_valid.SelectionRangeSelector);
		Assert.IsNull(s_valid.DetailSelector);
	}

	[TestMethod]
	public void WithEverySelector_KeepsTheSuppliedSelectors()
	{
		static object? dataSelector(string text) => text.Length;
		static TextRange? rangeSelector(string _) => new TextRange(0, 1);
		static TextRange? selectionRangeSelector(string _) => new TextRange(0, 1);
		static string? detailSelector(string _) => "detail";

		var hooks = new DocumentSymbolOutlineHooks<string>
		{
			NameSelector = text => text,
			KindSelector = _ => DocumentSymbolKind.Variable,
			DataSelector = dataSelector,
			RangeSelector = rangeSelector,
			SelectionRangeSelector = selectionRangeSelector,
			DetailSelector = detailSelector,
		};

		Assert.AreSame(dataSelector, hooks.DataSelector);
		Assert.AreSame(rangeSelector, hooks.RangeSelector);
		Assert.AreSame(selectionRangeSelector, hooks.SelectionRangeSelector);
		Assert.AreSame(detailSelector, hooks.DetailSelector);
	}
}
