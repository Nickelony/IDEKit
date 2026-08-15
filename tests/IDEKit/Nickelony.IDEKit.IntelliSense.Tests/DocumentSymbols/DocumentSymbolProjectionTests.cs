using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.IDEKit.IntelliSense.Tests.DocumentSymbols;

[TestClass]
public sealed class DocumentSymbolProjectionTests
{
	[TestMethod]
	public void Constructor_NullNameSelector_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => new DocumentSymbolProjection<string>(null!, _ => TextDocumentSymbolKind.Variable));

		Assert.AreEqual("nameSelector", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_NullKindSelector_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => new DocumentSymbolProjection<string>(text => text, null!));

		Assert.AreEqual("kindSelector", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_WithoutOptionalSelectors_LeavesThemNull()
	{
		var projection = new DocumentSymbolProjection<string>(text => text, _ => TextDocumentSymbolKind.Variable);

		Assert.IsNull(projection.DataSelector);
		Assert.IsNull(projection.RangeSelector);
		Assert.IsNull(projection.SelectionRangeSelector);
		Assert.IsNull(projection.DetailSelector);
	}

	[TestMethod]
	public void Constructor_WithEverySelector_KeepsTheSuppliedSelectors()
	{
		Func<string, object?> dataSelector = text => text.Length;
		Func<string, TextRange?> rangeSelector = _ => new TextRange(0, 1);
		Func<string, TextRange?> selectionRangeSelector = _ => new TextRange(0, 1);
		Func<string, string?> detailSelector = _ => "detail";

		var projection = new DocumentSymbolProjection<string>(
			text => text,
			_ => TextDocumentSymbolKind.Variable,
			dataSelector,
			rangeSelector,
			selectionRangeSelector,
			detailSelector);

		Assert.AreSame(dataSelector, projection.DataSelector);
		Assert.AreSame(rangeSelector, projection.RangeSelector);
		Assert.AreSame(selectionRangeSelector, projection.SelectionRangeSelector);
		Assert.AreSame(detailSelector, projection.DetailSelector);
	}
}
