using System.Text.Json;
using System.Text.Json.Nodes;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Caches the host's settings as one serialized element that the settings push and the configuration answers share.</summary>
/// <remarks>
/// The snapshot is captured lazily from the host, refreshed from the host before each settings push,
/// and refreshed whenever a <c>workspace/didChangeConfiguration</c> notification is sent successfully, so the pushed notification
/// and the <c>workspace/configuration</c> answers carry the same settings. The host's settings factory runs synchronously under the snapshot lock,
/// so it must be cheap, non-blocking, and must not re-enter the client.
/// </remarks>
internal sealed class SettingsSnapshotCache
{
	private readonly object _settingsSnapshotSyncRoot = new();

	private readonly ILogger _logger;
	private readonly Func<JsonNode> _settingsProvider;

	private CachedSettingsSnapshot? _cachedSettingsSnapshot;

	/// <param name="settingsProvider">Produces the current settings document.</param>
	/// <param name="logger">The logger for the faults of the host's settings factory and of an outgoing settings notification.</param>
	internal SettingsSnapshotCache(Func<JsonNode> settingsProvider, ILogger logger)
	{
		_settingsProvider = settingsProvider;
		_logger = logger;
	}

	/// <summary>The serialized settings element of the snapshot, which the first read captures from the host.</summary>
	/// <exception cref="ArgumentNullException">The host's settings factory returned <see langword="null"/>.</exception>
	internal JsonElement Current => GetCachedSettingsSnapshot().SettingsElement;

	private CachedSettingsSnapshot GetCachedSettingsSnapshot()
	{
		// The provider is invoked under the lock so two concurrent configuration requests cannot run it at once
		// and then race to store their results, discarding the loser's snapshot after its provider call had
		// already happened. The lock is reentrant, so a provider that re-enters this type on the same thread is safe.
		lock (_settingsSnapshotSyncRoot)
			return _cachedSettingsSnapshot ??= CreateCachedSettingsSnapshotFromProvider();
	}

	private CachedSettingsSnapshot RefreshCachedSettingsSnapshotFromProvider()
	{
		lock (_settingsSnapshotSyncRoot)
			return _cachedSettingsSnapshot = CreateCachedSettingsSnapshotFromProvider();
	}

	private CachedSettingsSnapshot CreateCachedSettingsSnapshotFromProvider()
	{
		JsonNode settingsPayload = _settingsProvider();
		ArgumentNullException.ThrowIfNull(settingsPayload);

		return CreateCachedSettingsSnapshot(settingsPayload);
	}

	/// <summary>
	/// Refreshes the cached settings snapshot from the host and returns the serialized settings element shared
	/// by the <c>workspace/didChangeConfiguration</c> push and the <c>workspace/configuration</c> answers.
	/// </summary>
	internal JsonElement RefreshFromProvider() => RefreshCachedSettingsSnapshotFromProvider().SettingsElement;

	/// <summary>Refreshes the cached settings snapshot from the host, containing a fault of the host's settings factory.</summary>
	/// <param name="settingsElement">The serialized settings element when the refresh succeeded; otherwise, <see langword="default"/>.</param>
	/// <returns><see langword="true"/> when the host produced the settings; otherwise, <see langword="false"/> after logging the fault.</returns>
	internal bool TryRefreshFromProvider(out JsonElement settingsElement)
	{
		try
		{
			settingsElement = RefreshFromProvider();
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "The settings provider failed; the workspace/didChangeConfiguration push was skipped.");

			settingsElement = default;
			return false;
		}
	}

	private CachedSettingsSnapshot CacheSettingsSnapshot(object settingsPayload)
	{
		CachedSettingsSnapshot settingsSnapshot = CreateCachedSettingsSnapshot(settingsPayload);

		lock (_settingsSnapshotSyncRoot)
		{
			_cachedSettingsSnapshot = settingsSnapshot;
			return settingsSnapshot;
		}
	}

	private static CachedSettingsSnapshot CreateCachedSettingsSnapshot(object settingsPayload)
	{
		JsonElement settingsElement = settingsPayload is JsonElement jsonElement
			? jsonElement.Clone()
			: JsonSerializer.SerializeToElement(settingsPayload, ProtocolSerialization.ForPayloads());

		return new(settingsElement);
	}

	/// <summary>
	/// Normalizes an outgoing <c>workspace/didChangeConfiguration</c> payload to the serialized settings element the cache
	/// and the <c>workspace/configuration</c> answers share, so the pushed notification and the configuration answers use the same member casing.
	/// </summary>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="parameters">The notification payload supplied by the host.</param>
	/// <returns>The normalized notification payload, or the original payload for other methods or when normalization fails.</returns>
	internal object? NormalizeDidChangeConfigurationParameters(string method, object? parameters)
	{
		if (parameters is null || !string.Equals(method, LspMethodNames.DidChangeConfiguration, StringComparison.Ordinal))
			return parameters;

		try
		{
			if (parameters is DidChangeConfigurationParams didChangeConfigurationParameters)
				return new DidChangeConfigurationParams(CreateCachedSettingsSnapshot(didChangeConfigurationParameters.Settings).SettingsElement);

			JsonElement payload = JsonSerializer.SerializeToElement(parameters, ProtocolSerialization.ForPayloads());

			if (!JsonElementReader.TryGetProperty(payload, "settings", out JsonElement settingsElement))
				return parameters;

			return new DidChangeConfigurationParams(settingsElement);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to normalize an outgoing didChangeConfiguration notification; sending the payload as supplied.");
			return parameters;
		}
	}

	internal void RefreshFromNotification(string method, object? parameters)
	{
		if (parameters is null || !string.Equals(method, LspMethodNames.DidChangeConfiguration, StringComparison.Ordinal))
			return;

		try
		{
			if (parameters is DidChangeConfigurationParams didChangeConfigurationParameters)
			{
				CacheSettingsSnapshot(didChangeConfigurationParameters.Settings);
				return;
			}

			JsonElement payload = JsonSerializer.SerializeToElement(parameters, ProtocolSerialization.ForPayloads());

			if (!JsonElementReader.TryGetProperty(payload, "settings", out JsonElement settingsElement))
				return;

			CacheSettingsSnapshot(settingsElement);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(
				exception,
				"Failed to refresh the cached workspace settings snapshot from an outgoing didChangeConfiguration notification."
			);
		}
	}

	/// <summary>
	/// Stores the serialized settings element shared by the <c>workspace/didChangeConfiguration</c> push
	/// and the <c>workspace/configuration</c> answers.
	/// </summary>
	private sealed record CachedSettingsSnapshot(JsonElement SettingsElement);
}
