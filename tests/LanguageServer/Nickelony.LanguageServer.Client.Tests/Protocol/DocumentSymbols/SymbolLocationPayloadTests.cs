using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SymbolLocationPayloadTests
{
	private static readonly RangePayload s_range = new(new PositionPayload(1, 2), new PositionPayload(3, 4));

	[TestMethod]
	public void Constructor_NullUri_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new SymbolLocationPayload(null!, s_range));

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	public void Constructor_BlankUri_Throws(string uri)
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => new SymbolLocationPayload(uri, s_range));
		Assert.AreEqual("uri", exception.ParamName);
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	public void WithExpression_BlankUri_Throws(string uri)
	{
		var location = new SymbolLocationPayload("file:///a.lua", s_range);
		Assert.ThrowsExactly<ArgumentException>(() => location with { Uri = uri });
	}

	[TestMethod]
	public void WithExpression_ValidUri_ReplacesOnlyTheUri()
	{
		var location = new SymbolLocationPayload("file:///a.lua", s_range);

		SymbolLocationPayload moved = location with { Uri = "file:///b.lua" };

		Assert.AreEqual("file:///b.lua", moved.Uri);
		Assert.AreEqual(s_range, moved.Range);
		Assert.AreEqual("file:///a.lua", location.Uri);
	}

	[TestMethod]
	public void Deconstruct_ReturnsTheUriAndTheRange()
	{
		(string uri, RangePayload range) = new SymbolLocationPayload("file:///a.lua", s_range);

		Assert.AreEqual("file:///a.lua", uri);
		Assert.AreEqual(s_range, range);
	}

	[TestMethod]
	public void Equality_ComparesTheMembers()
	{
		Assert.AreEqual(new SymbolLocationPayload("file:///a.lua", s_range), new SymbolLocationPayload("file:///a.lua", s_range));
		Assert.AreNotEqual(new SymbolLocationPayload("file:///a.lua", s_range), new SymbolLocationPayload("file:///b.lua", s_range));
	}

	[TestMethod]
	public void Serialize_UsesTheWireMemberNames()
	{
		string json = JsonSerializer.Serialize(new SymbolLocationPayload("file:///a.lua", s_range));

		using var document = JsonDocument.Parse(json);

		Assert.AreEqual("file:///a.lua", document.RootElement.GetProperty("uri").GetString());
		Assert.AreEqual(JsonValueKind.Object, document.RootElement.GetProperty("range").ValueKind);
	}
}
