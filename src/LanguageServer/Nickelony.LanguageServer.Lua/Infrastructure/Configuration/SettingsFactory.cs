using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Builds the LuaLS settings payload sent by the provider.
/// </summary>
/// <remarks>
/// Two values are fixed rather than host-settable, because the provider consumes the behavior they
/// gate: <c>workspace.checkThirdParty</c> is always <c>"Disable"</c> (a library cannot answer
/// LuaLS's interactive prompts) and <c>completion.callSnippet</c> is always <c>"Replace"</c> (the
/// provider consumes snippet insert texts end to end). Expose either as an option only when a host
/// needs to change it; until then a knob would be unproduced surface.
/// </remarks>
internal static class SettingsFactory
{
	private const string DisabledSettingValue = "Disable";
	private const string CallSnippetSettingValue = "Replace";

	/// <summary>
	/// Builds the LuaLS settings payload for the current workspace.
	/// </summary>
	/// <param name="options">The provider options that override the default settings values.</param>
	/// <returns>
	/// The settings document serialized into the <c>workspace/didChangeConfiguration</c> payload and served for
	/// <c>workspace/configuration</c> callbacks. The root key is the LuaLS-canonical <c>Lua</c> section, declared
	/// explicitly so the protocol's camelCase naming policy cannot lower it to <c>lua</c>; every nested key keeps
	/// the policy's camelCase spelling.
	/// </returns>
	internal static object Create(LuaLanguageServerOptions options)
	{
		List<string> library = CopyNonBlankEntries(options.AdditionalLibraryDirectories);
		List<string> disabledDiagnostics = CopyNonBlankEntries(options.DisabledDiagnostics);

		object lua = new
		{
			runtime = new
			{
				version = options.RuntimeVersion
			},
			workspace = new
			{
				checkThirdParty = DisabledSettingValue,
				library
			},
			completion = new
			{
				// Call snippets are always enabled: the provider consumes snippet insert texts
				// end to end, and "Replace" offers the call snippet instead of the plain name.
				callSnippet = CallSnippetSettingValue
			},
			semantic = new
			{
				enable = options.EnableSemanticHighlighting,
				annotation = options.EnableSemanticAnnotationHighlighting,
				variable = options.EnableSemanticVariableHighlighting,
				keyword = options.EnableSemanticKeywordHighlighting
			},
			diagnostics = new
			{
				disable = disabledDiagnostics
			}
		};

		return new LuaSettingsDocument { Lua = lua };
	}

	/// <summary>
	/// Copies the non-blank entries of a settings list into a fresh list.
	/// </summary>
	/// <param name="entries">The configured entries.</param>
	/// <returns>A new list that preserves the entries' order and drops blank ones.</returns>
	/// <remarks>
	/// A hand-rolled filter avoids the LINQ iterator and delegate allocation on a path that runs per settings build.
	/// </remarks>
	private static List<string> CopyNonBlankEntries(IReadOnlyList<string> entries)
	{
		var result = new List<string>();

		foreach (string entry in entries)
		{
			if (!string.IsNullOrWhiteSpace(entry))
				result.Add(entry);
		}

		return result;
	}

	/// <summary>
	/// The settings document root. Its single root key is the LuaLS configuration section <c>Lua</c>, which is
	/// PascalCase on the wire.
	/// </summary>
	/// <remarks>
	/// Declaring the key explicitly overrides the client's camelCase naming policy, so the section cannot silently
	/// degrade to <c>lua</c> and the section reader no longer has to rely on its case-insensitive fallback.
	/// </remarks>
	private sealed record LuaSettingsDocument
	{
		[JsonPropertyName("Lua")]
		public required object Lua { get; init; }
	}
}
