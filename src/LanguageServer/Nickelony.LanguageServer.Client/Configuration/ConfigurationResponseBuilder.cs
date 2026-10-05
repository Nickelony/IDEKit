using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Builds the answer to a <c>workspace/configuration</c> request from the cached settings snapshot.</summary>
internal static class ConfigurationResponseBuilder
{
	/// <summary>Builds the configuration response payload requested by the language server.</summary>
	/// <param name="parameters">The requested configuration sections.</param>
	/// <param name="settings">The cache whose snapshot answers the sections; a request without items does not read it.</param>
	/// <param name="logger">The logger for the sections that cannot be answered.</param>
	/// <returns>
	/// The configuration response elements in request order, one per requested entry; a <see langword="null"/> or malformed request entry is answered
	/// with <see langword="null"/>.
	/// </returns>
	internal static JsonElement?[] Build(WorkspaceConfigurationParams parameters, SettingsSnapshotCache settings, ILogger logger)
	{
		WorkspaceConfigurationItemPayload[] items = parameters.Items ?? [];

		if (items.Length == 0)
			return [];

		try
		{
			JsonElement settingsElement = settings.Current;
			var results = new JsonElement?[items.Length];

			for (int i = 0; i < items.Length; i++)
			{
				// A null or malformed request entry has no section to resolve, and the placeholder keeps the
				// response aligned one-for-one with the request as LSP requires.
				if (items[i].IsMalformed)
				{
					results[i] = null;
					continue;
				}

				try
				{
					results[i] = JsonConfigurationSectionReader.GetSection(settingsElement, items[i].Section);
				}
				catch (Exception exception)
				{
					logger.LogWarning(
						exception,
						"Failed to extract workspace/configuration section '{Section}'; returning null for that section.",
						string.IsNullOrWhiteSpace(items[i].Section) ? "<root>" : items[i].Section
					);

					results[i] = null;
				}
			}

			return results;
		}
		catch (Exception exception)
		{
			logger.LogWarning(
				exception,
				"Failed to build the workspace/configuration response; returning null values for {SectionCount} requested section(s).",
				items.Length
			);

			return new JsonElement?[items.Length];
		}
	}
}
