using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Builds the settings document the client serves for the Roslyn language server's <c>workspace/configuration</c> requests and pushes
/// through <c>workspace/didChangeConfiguration</c>.
/// </summary>
/// <remarks>
/// The document is keyed by the exact section names the server asks for. Both language prefixes are always present, because the server
/// fetches the value for both C# and Visual Basic even when a provider serves only one of them. A section the document does not carry
/// resolves to <see langword="null"/>, which leaves the server's own default in place.
/// </remarks>
internal static class SettingsBuilder
{
	private const string CompletionGroupName = "completion";

	private const string ShowCompletionItemsFromUnimportedNamespacesConfigName = "dotnet_show_completion_items_from_unimported_namespaces";

	private const string CSharpConfigurationPrefix = "csharp";

	private const string VisualBasicConfigurationPrefix = "visual_basic";

	private const string CSharpCompletionSectionName = CSharpConfigurationPrefix + "|" + CompletionGroupName;

	private const string VisualBasicCompletionSectionName = VisualBasicConfigurationPrefix + "|" + CompletionGroupName;

	/// <summary>Builds the settings document for one session.</summary>
	/// <param name="options">The session options that select the settings values.</param>
	/// <returns>The settings document serialized into the configuration payloads.</returns>
	internal static JsonNode Build(RoslynLanguageServerOptions options)
	{
		return new JsonObject
		{
			[CSharpCompletionSectionName] = CreateCompletionSection(options),
			[VisualBasicCompletionSectionName] = CreateCompletionSection(options),
		};
	}

	/// <summary>Builds the completion option group of one language section.</summary>
	/// <param name="options">The session options that select the settings values.</param>
	/// <returns>A new group; a JSON node has one parent, so each language section gets its own.</returns>
	private static JsonObject CreateCompletionSection(RoslynLanguageServerOptions options) =>
		new() { [ShowCompletionItemsFromUnimportedNamespacesConfigName] = options.ShowCompletionItemsFromUnimportedNamespaces };
}
