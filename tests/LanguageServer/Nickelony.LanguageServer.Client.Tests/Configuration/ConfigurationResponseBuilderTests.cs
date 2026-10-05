using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ConfigurationResponseBuilderTests
{
	private static SettingsSnapshotCache CreateCache(Func<JsonNode> settingsProvider) => new(settingsProvider, NullLogger.Instance);

	private static JsonElement?[] Answer(SettingsSnapshotCache cache, WorkspaceConfigurationParams parameters, ILogger? logger = null) =>
		ConfigurationResponseBuilder.Build(parameters, cache, logger ?? NullLogger.Instance);

	private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

	private static WorkspaceConfigurationParams Sections(params string[] sections) =>
		new([.. sections.Select(section => new WorkspaceConfigurationItemPayload(section))]);

	[TestMethod]
	public void Build_ReturnsRequestedSections()
	{
		SettingsSnapshotCache cache = CreateCache(static () => Parse("""{ "Example": { "runtime": { "version": "4.0" } } }"""));

		JsonElement?[] response = Answer(cache, Sections("Example", "Example.runtime"));

		Assert.HasCount(2, response);
		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(response[1]).GetProperty("version").GetString());
	}

	[TestMethod]
	public void Build_ReturnsRequestedNestedSections()
	{
		SettingsSnapshotCache cache = CreateCache(static () => Parse("""{ "Editor": { "theme": "Dark", "fonts": { "size": 14 } } }"""));

		JsonElement?[] response = Answer(cache, Sections("Editor", "Editor.fonts"));

		Assert.HasCount(2, response);
		Assert.AreEqual("Dark", JsonSerializer.SerializeToElement(response[0]).GetProperty("theme").GetString());
		Assert.AreEqual(14, JsonSerializer.SerializeToElement(response[1]).GetProperty("size").GetInt32());
	}

	[TestMethod]
	public void Build_ReturnsNullWhenSectionIsMissing()
	{
		SettingsSnapshotCache cache = CreateCache(static () => Parse("""{ "Editor": { "theme": "Dark" } }"""));

		JsonElement?[] response = Answer(cache, Sections("Example", "Example.runtime"));

		Assert.HasCount(2, response);
		Assert.IsNull(response[0]);
		Assert.IsNull(response[1]);
	}

	[TestMethod]
	public void Build_ReusesCachedSettingsSnapshotAcrossRepeatedRequests()
	{
		int settingsProviderCallCount = 0;

		SettingsSnapshotCache cache = CreateCache(() =>
		{
			settingsProviderCallCount++;
			return Parse("""{ "Example": { "runtime": { "version": "4.0" } } }""");
		});

		JsonElement?[] firstResponse = Answer(cache, Sections("Example.runtime"));
		JsonElement?[] secondResponse = Answer(cache, Sections("Example.runtime"));

		Assert.AreEqual(1, settingsProviderCallCount);
		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(firstResponse[0]).GetProperty("version").GetString());
		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(secondResponse[0]).GetProperty("version").GetString());
	}

	[TestMethod]
	public void Build_MatchesNestedSectionsCaseInsensitively()
	{
		SettingsSnapshotCache cache = CreateCache(static () => Parse("""{ "Section": { "Runtime": { "Version": "5.4" } } }"""));

		JsonElement?[] response = Answer(cache, Sections("section.runtime"));

		Assert.HasCount(1, response);
		Assert.IsNotNull(response[0]);
		Assert.AreEqual("5.4", JsonSerializer.SerializeToElement(response[0]).GetProperty("Version").GetString());
	}

	[TestMethod]
	public void Build_MalformedEntry_KeepsPositionAndAnswersNull()
	{
		SettingsSnapshotCache cache = CreateCache(static () => Parse("""{ "Section": { "Runtime": { "Version": "5.4" } } }"""));

		JsonElement?[] response = Answer(
			cache,

			new WorkspaceConfigurationParams([
				new WorkspaceConfigurationItemPayload("Section"),
				new WorkspaceConfigurationItemPayload(Section: null, IsMalformed: true),
				new WorkspaceConfigurationItemPayload("Section.runtime"),
			])
		);

		// The response must stay aligned one-for-one with the request: the malformed placeholder keeps its
		// slot and is answered with null instead of shifting every later section.
		Assert.HasCount(3, response);
		Assert.IsNotNull(response[0]);
		Assert.IsNull(response[1]);
		Assert.IsNotNull(response[2]);
		Assert.AreEqual("5.4", JsonSerializer.SerializeToElement(response[2]).GetProperty("Version").GetString());
	}

	[TestMethod]
	public void Build_WithoutItems_ReturnsNothingAndDoesNotCallTheProvider()
	{
		int settingsProviderCallCount = 0;

		SettingsSnapshotCache cache = CreateCache(() =>
		{
			settingsProviderCallCount++;
			return new JsonObject();
		});

		Assert.IsEmpty(Answer(cache, new WorkspaceConfigurationParams([])));
		Assert.AreEqual(0, settingsProviderCallCount);
	}

	[TestMethod]
	public void Build_WhenTheProviderThrows_AnswersNullForEveryEntryAndWarns()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		SettingsSnapshotCache cache = CreateCache(static () => throw new InvalidOperationException("Simulated provider failure."));

		JsonElement?[] response = Answer(cache, Sections("Example", "Example.runtime"), logger);

		Assert.HasCount(2, response);
		Assert.IsNull(response[0]);
		Assert.IsNull(response[1]);
		Assert.Contains(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("returning null values"), logger.Logs);
	}
}
