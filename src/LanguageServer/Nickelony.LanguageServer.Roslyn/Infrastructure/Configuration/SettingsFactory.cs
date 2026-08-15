using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Builds the settings document the provider serves for the Roslyn language server's
/// <c>workspace/configuration</c> requests and pushes through <c>workspace/didChangeConfiguration</c>.
/// </summary>
/// <remarks>
/// <para>
/// The server reads its options through <c>workspace/configuration</c>, requesting each option by a dotted
/// section name. A per-language option is prefixed with the language's configuration name (<c>csharp|</c> or
/// <c>visual_basic|</c>), and its section is <c>{prefix}|{group}.{configName}</c>; the client resolves a section
/// by splitting it on <c>.</c> and walking nested properties, so the document is keyed by the exact section
/// names the server asks for. Both language prefixes are always present, because the server fetches the value
/// for both C# and Visual Basic even when a provider serves only one of them. A section the document does not
/// carry resolves to <see langword="null"/>, which leaves the server's own default in place.
/// </para>
/// <para>
/// Only options whose section name is fixed by the server are modeled here; the remaining options keep the
/// server's defaults until a host needs them, so the payload never claims a setting the provider cannot
/// describe accurately.
/// </para>
/// </remarks>
internal static class SettingsFactory
{
	private const string CompletionGroupName = "completion";

	private const string ShowCompletionItemsFromUnimportedNamespacesConfigName = "dotnet_show_completion_items_from_unimported_namespaces";

	private const string CSharpConfigurationPrefix = "csharp";

	private const string VisualBasicConfigurationPrefix = "visual_basic";

	/// <summary>
	/// Builds the settings document for one session.
	/// </summary>
	/// <param name="options">The session options that select the settings values.</param>
	/// <returns>The settings document serialized into the configuration payloads.</returns>
	internal static object Create(RoslynLanguageServerOptions options)
	{
		var completion = new CompletionSettingsSection
		{
			ShowCompletionItemsFromUnimportedNamespaces = options.ShowCompletionItemsFromUnimportedNamespaces
		};

		return new RoslynSettingsDocument
		{
			CSharpCompletion = completion,
			VisualBasicCompletion = completion
		};
	}

	/// <summary>
	/// The settings document root, keyed by the exact section names the server requests.
	/// </summary>
	/// <remarks>
	/// Each root key combines the language configuration prefix with the <c>completion</c> option group and is
	/// declared explicitly so the client's camelCase naming policy cannot alter it.
	/// </remarks>
	private sealed record RoslynSettingsDocument
	{
		[JsonPropertyName(CSharpConfigurationPrefix + "|" + CompletionGroupName)]
		public required CompletionSettingsSection CSharpCompletion { get; init; }

		[JsonPropertyName(VisualBasicConfigurationPrefix + "|" + CompletionGroupName)]
		public required CompletionSettingsSection VisualBasicCompletion { get; init; }
	}

	/// <summary>
	/// The completion option group within one language section.
	/// </summary>
	private sealed record CompletionSettingsSection
	{
		[JsonPropertyName(ShowCompletionItemsFromUnimportedNamespacesConfigName)]
		public required bool ShowCompletionItemsFromUnimportedNamespaces { get; init; }
	}
}
