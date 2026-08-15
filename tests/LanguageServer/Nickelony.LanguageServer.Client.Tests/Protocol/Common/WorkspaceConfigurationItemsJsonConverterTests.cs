using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Pins the position-preserving binding of <c>workspace/configuration</c> request items: a null or
/// malformed entry keeps its slot as a malformed placeholder so later entries stay aligned with the
/// request.
/// </summary>
[TestClass]
public sealed class WorkspaceConfigurationItemsJsonConverterTests
{
	[TestMethod]
	public void Read_NullAndMalformedEntries_KeepTheirPositions()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItem[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItem[]>(
			""" [{"section":"Editor"}, null, {"section":123}, {"section":"Editor.fonts"}] """,
			options);

		Assert.IsNotNull(items);
		Assert.AreEqual(4, items.Length);
		Assert.AreEqual("Editor", items[0].Section);
		Assert.IsFalse(items[0].IsMalformed);
		Assert.IsTrue(items[1].IsMalformed);
		Assert.IsTrue(items[2].IsMalformed);
		Assert.AreEqual("Editor.fonts", items[3].Section);
		Assert.IsFalse(items[3].IsMalformed);
	}

	[TestMethod]
	public void Read_ObjectWithoutSection_RequestsTheRootAsNotMalformed()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItem[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItem[]>("[{}]", options);

		Assert.IsNotNull(items);
		Assert.AreEqual(1, items.Length);
		Assert.IsNull(items[0].Section);
		Assert.IsFalse(items[0].IsMalformed);
	}

	[TestMethod]
	public void Read_SectionNameWithDifferentCasing_IsMatchedCaseInsensitively()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItem[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItem[]>(
			""" [{"SECTION":"Editor.fonts"}] """,
			options);

		Assert.IsNotNull(items);
		Assert.AreEqual(1, items.Length);
		Assert.AreEqual("Editor.fonts", items[0].Section);
		Assert.IsFalse(items[0].IsMalformed);
	}

	[TestMethod]
	public void Read_NonArrayRoot_DegradesToAnEmptyCollection()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItem[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItem[]>("{\"items\":[]}", options);

		Assert.IsNotNull(items);
		Assert.AreEqual(0, items.Length);
	}
}
