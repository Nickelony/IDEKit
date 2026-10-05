using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SettingsSnapshotCacheTests
{
	private static SettingsSnapshotCache CreateCache(Func<JsonNode> settingsProvider, ILogger? logger = null) =>
		new(settingsProvider, logger ?? NullLogger.Instance);

	private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

	[TestMethod]
	public void RefreshFromProvider_CallsTheProviderAgainAndReplacesTheSnapshot()
	{
		int version = 0;

		SettingsSnapshotCache cache = CreateCache(() => new JsonObject { ["example"] = new JsonObject { ["version"] = ++version } });

		Assert.AreEqual(1, JsonSerializer.SerializeToElement(JsonConfigurationSectionReader.GetSection(cache.Current, "Example.version")).GetInt32());

		JsonElement refreshed = cache.RefreshFromProvider();

		Assert.AreEqual(2, refreshed.GetProperty("example").GetProperty("version").GetInt32());
		Assert.AreEqual(2, JsonSerializer.SerializeToElement(JsonConfigurationSectionReader.GetSection(cache.Current, "Example.version")).GetInt32());
	}

	[TestMethod]
	public void RefreshFromProvider_KeepsTheMemberNamesAsSpelled()
	{
		SettingsSnapshotCache cache = CreateCache(static () => Parse("""{ "MaxPreload": 4 }"""));

		JsonElement refreshed = cache.RefreshFromProvider();

		Assert.AreEqual(4, refreshed.GetProperty("MaxPreload").GetInt32());
		Assert.IsFalse(refreshed.TryGetProperty("maxPreload", out _));
	}

	[TestMethod]
	public void TryRefreshFromProvider_WhenTheProviderSucceeds_ReturnsTheRefreshedElementAndReplacesTheSnapshot()
	{
		int version = 0;

		SettingsSnapshotCache cache = CreateCache(() => new JsonObject { ["version"] = ++version });

		_ = cache.Current;

		bool refreshed = cache.TryRefreshFromProvider(out JsonElement settings);

		Assert.IsTrue(refreshed);
		Assert.AreEqual(2, settings.GetProperty("version").GetInt32());
		Assert.AreEqual(2, JsonSerializer.SerializeToElement(JsonConfigurationSectionReader.GetSection(cache.Current, "version")).GetInt32());
	}

	[TestMethod]
	public void TryRefreshFromProvider_WhenTheProviderThrows_ReturnsFalseKeepsTheSnapshotAndWarns()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		bool shouldThrow = false;

		SettingsSnapshotCache cache = CreateCache(
			() => shouldThrow ? throw new InvalidOperationException("Simulated provider failure.") : Parse("""{ "version": "4.0" }"""),
			logger
		);

		_ = cache.Current;

		shouldThrow = true;

		bool refreshed = cache.TryRefreshFromProvider(out JsonElement settings);

		Assert.IsFalse(refreshed);
		Assert.AreEqual(JsonValueKind.Undefined, settings.ValueKind);
		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(JsonConfigurationSectionReader.GetSection(cache.Current, "version")).GetString());
		Assert.Contains(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("settings provider failed"), logger.Logs);
	}

	[TestMethod]
	public void TryRefreshFromProvider_WhenTheProviderReturnsNull_ReturnsFalseAndWarns()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		SettingsSnapshotCache cache = CreateCache(static () => null!, logger);

		Assert.IsFalse(cache.TryRefreshFromProvider(out _));
		Assert.Contains(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("settings provider failed"), logger.Logs);
	}

	[TestMethod]
	public void NormalizeDidChangeConfigurationParameters_ForAnotherMethod_ReturnsThePayloadUnchanged()
	{
		SettingsSnapshotCache cache = CreateCache(static () => new JsonObject());
		var payload = new { UnrelatedMember = "value" };

		Assert.AreSame(payload, cache.NormalizeDidChangeConfigurationParameters("workspace/didChangeWatchedFiles", payload));
	}

	[TestMethod]
	public void NormalizeDidChangeConfigurationParameters_ForATypedPayload_NormalizesTheSettingsToLowerCamelNames()
	{
		SettingsSnapshotCache cache = CreateCache(static () => new JsonObject());

		object? normalized = cache.NormalizeDidChangeConfigurationParameters(
			"workspace/didChangeConfiguration",
			new DidChangeConfigurationParams(new PascalCaseSettings { MaxPreload = 4 })
		);

		var parameters = (DidChangeConfigurationParams)normalized!;

		Assert.AreEqual(4, JsonSerializer.SerializeToElement(parameters.Settings).GetProperty("maxPreload").GetInt32());
	}

	[TestMethod]
	public void NormalizeDidChangeConfigurationParameters_ForAnObjectWithSettings_WrapsTheSettings()
	{
		SettingsSnapshotCache cache = CreateCache(static () => new JsonObject());

		object? normalized = cache.NormalizeDidChangeConfigurationParameters(
			"workspace/didChangeConfiguration",
			new { Settings = new PascalCaseSettings { MaxPreload = 7 } }
		);

		var parameters = (DidChangeConfigurationParams)normalized!;

		Assert.AreEqual(7, JsonSerializer.SerializeToElement(parameters.Settings).GetProperty("maxPreload").GetInt32());
	}

	[TestMethod]
	public void NormalizeDidChangeConfigurationParameters_ForAnObjectWithoutSettings_ReturnsThePayloadUnchanged()
	{
		SettingsSnapshotCache cache = CreateCache(static () => new JsonObject());
		var payload = new { UnrelatedMember = "value" };

		Assert.AreSame(payload, cache.NormalizeDidChangeConfigurationParameters("workspace/didChangeConfiguration", payload));
	}

	[TestMethod]
	public void RefreshFromNotification_StoresTheNotifiedSettingsWithoutCallingTheProviderAgain()
	{
		int settingsProviderCallCount = 0;

		SettingsSnapshotCache cache = CreateCache(() =>
		{
			settingsProviderCallCount++;
			return Parse("""{ "Example": { "version": "4.0" } }""");
		});

		_ = cache.Current;

		cache.RefreshFromNotification(
			"workspace/didChangeConfiguration",
			new DidChangeConfigurationParams(new { Example = new { Version = "4.1" } })
		);

		JsonElement? section = JsonConfigurationSectionReader.GetSection(cache.Current, "Example.version");

		Assert.AreEqual(1, settingsProviderCallCount);
		Assert.AreEqual("4.1", JsonSerializer.SerializeToElement(section).GetString());
	}

	[TestMethod]
	public void RefreshFromNotification_ForAnotherMethod_KeepsTheSnapshot()
	{
		SettingsSnapshotCache cache = CreateCache(static () => Parse("""{ "Example": { "version": "4.0" } }"""));

		_ = cache.Current;

		cache.RefreshFromNotification("workspace/didChangeWatchedFiles", new DidChangeConfigurationParams(new { Example = new { Version = "9.9" } }));

		Assert.AreEqual(
			"4.0",
			JsonSerializer.SerializeToElement(JsonConfigurationSectionReader.GetSection(cache.Current, "Example.version")).GetString()
		);
	}

	private sealed class PascalCaseSettings
	{
		public int MaxPreload { get; init; }
	}
}
