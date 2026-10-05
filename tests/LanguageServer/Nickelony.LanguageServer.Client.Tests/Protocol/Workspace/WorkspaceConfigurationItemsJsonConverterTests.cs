using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Pins the position-preserving binding of <c>workspace/configuration</c> request items: a <see langword="null"/> or malformed entry keeps its slot
/// as a malformed placeholder so later entries stay aligned with the request.
/// </summary>
[TestClass]
public sealed class WorkspaceConfigurationItemsJsonConverterTests
{
	[TestMethod]
	public void Deserialize_NullAndMalformedEntries_KeepTheirPositions()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItemPayload[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItemPayload[]>(
			""" [{"section":"Editor"}, null, {"section":123}, {"section":"Editor.fonts"}] """,
			options
		);

		Assert.IsNotNull(items);
		Assert.HasCount(4, items);
		Assert.AreEqual("Editor", items[0].Section);
		Assert.IsFalse(items[0].IsMalformed);
		Assert.IsTrue(items[1].IsMalformed);
		Assert.IsTrue(items[2].IsMalformed);
		Assert.AreEqual("Editor.fonts", items[3].Section);
		Assert.IsFalse(items[3].IsMalformed);
	}

	[TestMethod]
	public void Deserialize_ObjectWithoutSection_RequestsTheRootAsNotMalformed()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItemPayload[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItemPayload[]>("[{}]", options);

		Assert.IsNotNull(items);
		Assert.HasCount(1, items);
		Assert.IsNull(items[0].Section);
		Assert.IsFalse(items[0].IsMalformed);
	}

	[TestMethod]
	public void Deserialize_SectionNameWithDifferentCasing_IsMatchedCaseInsensitively()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItemPayload[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItemPayload[]>(
			""" [{"SECTION":"Editor.fonts"}] """,
			options
		);

		Assert.IsNotNull(items);
		Assert.HasCount(1, items);
		Assert.AreEqual("Editor.fonts", items[0].Section);
		Assert.IsFalse(items[0].IsMalformed);
	}

	[TestMethod]
	public void Deserialize_NonArrayRoot_DegradesToAnEmptyCollection()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(null));

		WorkspaceConfigurationItemPayload[]? items = JsonSerializer.Deserialize<WorkspaceConfigurationItemPayload[]>("{\"items\":[]}", options);

		Assert.IsNotNull(items);
		Assert.IsEmpty(items);
	}
}
