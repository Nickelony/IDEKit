using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Lua;

/// <summary>Builds the LuaLS settings document the client serves and pushes.</summary>
/// <remarks>
/// Two values are fixed rather than host-settable, because the provider
/// consumes the behavior they gate: <c>workspace.checkThirdParty</c> is always <c>"Disable"</c> (a library cannot answer LuaLS's interactive prompts)
/// and <c>completion.callSnippet</c> is always <c>"Replace"</c> (the provider consumes snippet insert texts end to end).
/// </remarks>
internal static class SettingsBuilder
{
	private const string DisabledSettingValue = "Disable";
	private const string CallSnippetSettingValue = "Replace";

	/// <summary>Builds the LuaLS settings document from the options.</summary>
	/// <param name="options">The Lua options that override the default settings values.</param>
	/// <returns>
	/// The settings document served for <c>workspace/configuration</c> requests and pushed in <c>workspace/didChangeConfiguration</c>. The root key
	/// is the LuaLS-canonical <c>Lua</c> section, and every member name is spelled exactly as LuaLS reads it.
	/// </returns>
	internal static JsonNode Build(LuaLanguageServerOptions options)
	{
		return new JsonObject
		{
			["Lua"] = new JsonObject
			{
				["runtime"] = new JsonObject { ["version"] = options.RuntimeVersion },

				["workspace"] = new JsonObject
				{
					["checkThirdParty"] = DisabledSettingValue,
					["library"] = CopyNonBlankEntries(options.AdditionalLibraryDirectories),
				},

				["completion"] = new JsonObject
				{
					// "Replace" offers the call snippet instead of the plain name.
					["callSnippet"] = CallSnippetSettingValue,
				},

				["semantic"] = new JsonObject
				{
					["enable"] = options.EnableSemanticHighlighting,
					["annotation"] = options.EnableSemanticAnnotationHighlighting,
					["variable"] = options.EnableSemanticVariableHighlighting,
					["keyword"] = options.EnableSemanticKeywordHighlighting,
				},

				["diagnostics"] = new JsonObject { ["disable"] = CopyNonBlankEntries(options.DisabledDiagnostics) },
			},
		};
	}

	/// <summary>Copies the non-blank entries of a settings list into a fresh JSON array.</summary>
	/// <param name="entries">The configured entries.</param>
	/// <returns>A new array that preserves the entries' order and drops blank ones.</returns>
	private static JsonArray CopyNonBlankEntries(IReadOnlyList<string> entries)
	{
		var result = new JsonArray();

		foreach (string entry in entries)
		{
			if (!string.IsNullOrWhiteSpace(entry))
				result.Add(entry);
		}

		return result;
	}
}
