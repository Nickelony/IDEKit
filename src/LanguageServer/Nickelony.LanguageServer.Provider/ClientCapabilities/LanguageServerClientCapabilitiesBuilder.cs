using System.Text.Json.Nodes;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Builds the client-capabilities payload for the initialize request, shared by the language packages.</summary>
/// <remarks>
/// The advertised set is bounded by what the shared provider framework consumes end to end: every capability maps to a payload the framework
/// parses and surfaces. A language supplies only its deltas through <see cref="LanguageServerClientCapabilitiesOptions"/> on its profile,
/// and the provider builds the payload for the client it creates.
/// </remarks>
public static class LanguageServerClientCapabilitiesBuilder
{
	// The advertised token types mirror the shared LSP semantic-token vocabulary, built from the token model in
	// Nickelony.IDEKit.LanguageFeatures so the legend cannot drift from it.
	private static readonly string[] s_supportedSemanticTokenTypes =
	[
		SemanticTokenTypes.Namespace,
		SemanticTokenTypes.Type,
		SemanticTokenTypes.Class,
		SemanticTokenTypes.Enum,
		SemanticTokenTypes.Interface,
		SemanticTokenTypes.Struct,
		SemanticTokenTypes.TypeParameter,
		SemanticTokenTypes.Parameter,
		SemanticTokenTypes.Variable,
		SemanticTokenTypes.Property,
		SemanticTokenTypes.EnumMember,
		SemanticTokenTypes.Event,
		SemanticTokenTypes.Function,
		SemanticTokenTypes.Method,
		SemanticTokenTypes.Macro,
		SemanticTokenTypes.Keyword,
		SemanticTokenTypes.Modifier,
		SemanticTokenTypes.Comment,
		SemanticTokenTypes.String,
		SemanticTokenTypes.Number,
		SemanticTokenTypes.Regexp,
		SemanticTokenTypes.Operator,
		SemanticTokenTypes.Decorator,
		SemanticTokenTypes.Label,
	];

	private static readonly string[] s_supportedSemanticTokenModifiers =
	[
		SemanticTokenModifiers.Declaration,
		SemanticTokenModifiers.Definition,
		SemanticTokenModifiers.Readonly,
		SemanticTokenModifiers.Static,
		SemanticTokenModifiers.Deprecated,
		SemanticTokenModifiers.Abstract,
		SemanticTokenModifiers.Async,
		SemanticTokenModifiers.Modification,
		SemanticTokenModifiers.Documentation,
		SemanticTokenModifiers.DefaultLibrary,
	];

	/// <summary>Builds the capabilities payload for the initialize request.</summary>
	/// <param name="options">The per-language deltas.</param>
	/// <returns>The capabilities payload.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	public static JsonObject Build(LanguageServerClientCapabilitiesOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		JsonObject workspace = new()
		{
			["workspaceFolders"] = true,
			["configuration"] = true,

			// workspaceEdit is omitted so the server keeps the simple changes form; the versioned form can
			// carry resource operations the workspace-edit model cannot represent.
			["didChangeWatchedFiles"] = new JsonObject { ["dynamicRegistration"] = false },

			// The server sends workspace/semanticTokens/refresh when a change invalidates cached token colors.
			// The flag is required: without it the server never sends that refresh, and cached colors stay stale after a configuration change.
			["semanticTokens"] = new JsonObject { ["refreshSupport"] = true },

			// The provider maps every symbol kind the protocol defines, and a server that lists a symbol without its range answers it
			// through workspaceSymbol/resolve.
			["symbol"] = new JsonObject
			{
				["dynamicRegistration"] = false,
				["symbolKind"] = new JsonObject { ["valueSet"] = SupportedSymbolKinds() },
				["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray("location.range") },
			},
		};

		JsonObject textDocument = new()
		{
			["completion"] = new JsonObject
			{
				["contextSupport"] = true,

				// Every advertised completion-item capability is consumed by the completion parser: the
				// deprecated tag, preselection, commit characters, insert/replace edits, and the resolve
				// properties a resolved item merges.
				["completionItem"] = new JsonObject
				{
					["snippetSupport"] = true,
					["commitCharactersSupport"] = true,
					["documentationFormat"] = new JsonArray("markdown", "plaintext"),
					["preselectSupport"] = true,
					["insertReplaceSupport"] = true,
					["tagSupport"] = new JsonObject { ["valueSet"] = new JsonArray((int)CompletionItemTag.Deprecated) },
					["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray("detail", "documentation", "additionalTextEdits") },
				},

				// The response converter resolves exactly these list defaults onto items that omit them.
				["completionList"] = new JsonObject { ["itemDefaults"] = new JsonArray("commitCharacters", "editRange", "insertTextFormat", "data") },
			},

			["hover"] = new JsonObject { ["contentFormat"] = new JsonArray("markdown", "plaintext") },
			["definition"] = new JsonObject { ["linkSupport"] = true },

			// The provider consumes the hierarchical DocumentSymbol shape, so the server may skip the flat form, and it maps every kind the protocol defines.
			["documentSymbol"] = new JsonObject
			{
				["hierarchicalDocumentSymbolSupport"] = true,
				["symbolKind"] = new JsonObject { ["valueSet"] = SupportedSymbolKinds() },
			},

			["codeAction"] = new JsonObject
			{
				["dynamicRegistration"] = false,

				// The provider consumes the literal CodeAction shape, so the server may skip the bare command form.
				["codeActionLiteralSupport"] = new JsonObject
				{
					["codeActionKind"] = new JsonObject { ["valueSet"] = ToJsonArray(options.CodeActionKinds) },
				},

				["isPreferredSupport"] = true,

				// A server that computes an action's edit on demand offers the action with `data` alone, and the provider sends `codeAction/resolve`
				// for the one action the user chooses; the properties name what the resolved action adds.
				["dataSupport"] = true,
				["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray("edit", "command") },
			},

			["references"] = new JsonObject { ["dynamicRegistration"] = false },
			["rename"] = new JsonObject { ["dynamicRegistration"] = false, ["prepareSupport"] = false },
			["formatting"] = new JsonObject { ["dynamicRegistration"] = false },

			// Versioned diagnostics are the precondition for the cached-diagnostics version fences.
			["publishDiagnostics"] = new JsonObject { ["versionSupport"] = true },
		};

		if (options.SupportsPullDiagnostics)
		{
			// Advertising the pull capability is what makes the server prefer textDocument/diagnostic over the
			// push model; workspace/diagnostic/refresh lets it re-query after a project change, and related
			// reports are consumed by the provider.
			workspace["diagnostics"] = new JsonObject { ["refreshSupport"] = true };
			textDocument["diagnostic"] = new JsonObject { ["dynamicRegistration"] = false, ["relatedDocumentSupport"] = true };
		}

		// Label-offset support is the precondition for the offset-pair parameter labels.
		textDocument["signatureHelp"] = new JsonObject
		{
			["signatureInformation"] = new JsonObject
			{
				["documentationFormat"] = new JsonArray("markdown", "plaintext"),
				["parameterInformation"] = new JsonObject { ["labelOffsetSupport"] = true },

				// The request context can carry an explicit null activeParameter, which 3.18 allows only when this is declared.
				["noActiveParameterSupport"] = true,
			},

			["contextSupport"] = true,
		};

		JsonArray tokenModifiers = ToJsonArray(s_supportedSemanticTokenModifiers);

		if (options.ExtraSemanticTokenModifiers is { Count: > 0 } extraModifiers)
		{
			foreach (string modifier in extraModifiers)
				tokenModifiers.Add(modifier);
		}

		textDocument["semanticTokens"] = new JsonObject
		{
			// The client implements full semantic-token requests only (a boolean capability, no result ids), so
			// it does not advertise a delta capability it could never exercise.
			["requests"] = new JsonObject { ["range"] = false, ["full"] = true },
			["tokenTypes"] = ToJsonArray(s_supportedSemanticTokenTypes),
			["tokenModifiers"] = tokenModifiers,
			["formats"] = new JsonArray("relative"),
			["multilineTokenSupport"] = false,
			["overlappingTokenSupport"] = false,
			["augmentsSyntaxTokens"] = true,
		};

		return new JsonObject { ["workspace"] = workspace, ["textDocument"] = textDocument };
	}

	private static JsonArray SupportedSymbolKinds()
	{
		JsonArray kinds = [];

		foreach (SymbolKind kind in Enum.GetValues<SymbolKind>())
			kinds.Add((int)kind);

		return kinds;
	}

	private static JsonArray ToJsonArray(IEnumerable<string> values)
	{
		JsonArray array = [];

		foreach (string value in values)
			array.Add(value);

		return array;
	}
}
