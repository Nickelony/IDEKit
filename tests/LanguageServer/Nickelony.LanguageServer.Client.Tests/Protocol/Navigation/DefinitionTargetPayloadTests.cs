namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class DefinitionTargetPayloadTests
{
	private static readonly RangePayload s_range = new(new PositionPayload(1, 2), new PositionPayload(3, 4));

	[TestMethod]
	public void Constructor_NullUri_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new DefinitionTargetPayload(null!, s_range));

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	public void Constructor_BlankUri_Throws(string uri)
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => new DefinitionTargetPayload(uri, s_range));
		Assert.AreEqual("uri", exception.ParamName);
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	public void WithExpression_BlankUri_Throws(string uri)
	{
		var target = new DefinitionTargetPayload("file:///a.lua", s_range);
		Assert.ThrowsExactly<ArgumentException>(() => target with { Uri = uri });
	}

	[TestMethod]
	public void Deconstruct_ReturnsEveryMember()
	{
		var selection = new RangePayload(new PositionPayload(1, 3), new PositionPayload(1, 6));
		var origin = new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 4));

		(string uri, RangePayload targetRange, RangePayload? selectionRange, RangePayload? originSelectionRange) = new DefinitionTargetPayload(
			Uri: "file:///a.lua",
			TargetRange: s_range,
			SelectionRange: selection,
			OriginSelectionRange: origin
		);

		Assert.AreEqual("file:///a.lua", uri);
		Assert.AreEqual(s_range, targetRange);
		Assert.AreEqual(selection, selectionRange);
		Assert.AreEqual(origin, originSelectionRange);
	}

	[TestMethod]
	public void Constructor_OmittedRanges_AreNull()
	{
		var target = new DefinitionTargetPayload("file:///a.lua", s_range);

		Assert.IsNull(target.SelectionRange);
		Assert.IsNull(target.OriginSelectionRange);
	}
}
