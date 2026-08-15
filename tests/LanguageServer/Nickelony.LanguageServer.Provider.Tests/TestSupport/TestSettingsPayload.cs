namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Settings payload returned by <see cref="TestLanguageServerProvider"/> so tests can assert the configuration
/// refresh notification's content.
/// </summary>
/// <param name="Enabled">The enabled flag carried by the payload.</param>
internal sealed record TestSettingsPayload(bool Enabled);
