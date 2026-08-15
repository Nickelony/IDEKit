using Nickelony.IDEKit.IntelliSense.SemanticTokens;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Describes the client capabilities advertised to LuaLS for the initialize request.
/// </summary>
internal static class ClientCapabilitiesFactory
{
	private static readonly string[] s_supportedDocumentationFormats = ["markdown", "plaintext"];

	// The protocol completion tags the parser maps; unknown tag values stay representable and are ignored.
	private static readonly CompletionItemTag[] s_supportedCompletionItemTags = [CompletionItemTag.Deprecated];

	// The completion-item properties the resolved payload merges back onto the unresolved item.
	private static readonly string[] s_resolvableCompletionItemProperties = ["detail", "documentation", "additionalTextEdits"];

	// The completion-list defaults the response converter resolves onto the items that omit them. Every entry maps
	// to a bound payload member, so nothing is advertised that the model cannot carry (an advertised default with
	// no bound member would be dropped when the item is materialized).
	private static readonly string[] s_supportedCompletionItemDefaults =
	[
		"commitCharacters", "editRange", "insertTextFormat", "data"
	];

	// Built from the shared constants so the advertised legend cannot drift from the token model
	// in Nickelony.IDEKit.IntelliSense.
	private static readonly string[] s_supportedSemanticTokenTypes =
	[
		TextSemanticTokenTypes.Namespace,
		TextSemanticTokenTypes.Type,
		TextSemanticTokenTypes.Class,
		TextSemanticTokenTypes.Enum,
		TextSemanticTokenTypes.Interface,
		TextSemanticTokenTypes.Struct,
		TextSemanticTokenTypes.TypeParameter,
		TextSemanticTokenTypes.Parameter,
		TextSemanticTokenTypes.Variable,
		TextSemanticTokenTypes.Property,
		TextSemanticTokenTypes.EnumMember,
		TextSemanticTokenTypes.Event,
		TextSemanticTokenTypes.Function,
		TextSemanticTokenTypes.Method,
		TextSemanticTokenTypes.Macro,
		TextSemanticTokenTypes.Keyword,
		TextSemanticTokenTypes.Modifier,
		TextSemanticTokenTypes.Comment,
		TextSemanticTokenTypes.String,
		TextSemanticTokenTypes.Number,
		TextSemanticTokenTypes.Regexp,
		TextSemanticTokenTypes.Operator,
		TextSemanticTokenTypes.Decorator,
		TextSemanticTokenTypes.Label
	];

	private static readonly string[] s_supportedSemanticTokenModifiers =
	[
		TextSemanticTokenModifiers.Declaration,
		TextSemanticTokenModifiers.Definition,
		TextSemanticTokenModifiers.Readonly,
		TextSemanticTokenModifiers.Static,
		TextSemanticTokenModifiers.Deprecated,
		TextSemanticTokenModifiers.Abstract,
		TextSemanticTokenModifiers.Async,
		TextSemanticTokenModifiers.Modification,
		TextSemanticTokenModifiers.Documentation,
		TextSemanticTokenModifiers.DefaultLibrary,
		GlobalSemanticTokenModifier
	];

	// The advertised kind set mirrors the kinds LuaLS itself serves. The provider maps kinds as
	// opaque strings, so the set only bounds which literal CodeAction objects LuaLS may return.
	private static readonly string[] s_supportedCodeActionKinds = ["", "quickfix", "refactor.rewrite", "refactor.extract"];

	/// <summary>
	/// The lua-language-server <c>global</c> modifier, which extends the LSP modifier set and is
	/// therefore declared by this language package rather than the shared token vocabulary.
	/// </summary>
	internal const string GlobalSemanticTokenModifier = "global";

	/// <summary>
	/// Builds the capabilities payload for the initialize request.
	/// </summary>
	/// <returns>An anonymous capabilities object serialized into the initialize request.</returns>
	internal static object Create()
	{
		return new
		{
			workspace = new
			{
				workspaceFolders = true,
				configuration = true,
				// workspaceEdit is deliberately omitted: advertising workspaceEdit.documentChanges would let a
				// server switch rename and code-action results to the versioned form, which can also carry
				// create/rename/delete resource operations the shared workspace-edit model cannot
				// represent; the client would have to drop those edits entirely (fail closed). Without
				// the flag LuaLS serves the simple `changes` form, which the parser always accepts.
				didChangeWatchedFiles = new { dynamicRegistration = false },
				// LuaLS only sends the server-driven workspace/semanticTokens/refresh notification when the
				// client advertises refresh support; without it configuration changes cannot invalidate the
				// cached token colors.
				semanticTokens = new { refreshSupport = true }
			},
			textDocument = new
			{
				completion = new
				{
					contextSupport = true,
					// Every advertised completion-item capability is consumed by the completion parser: the
					// deprecated tag, preselection, commit characters, insert/replace edits, and the resolve
					// properties a resolved item merges (detail, documentation, additional edits).
					completionItem = new
					{
						snippetSupport = true,
						commitCharactersSupport = true,
						documentationFormat = s_supportedDocumentationFormats,
						preselectSupport = true,
						insertReplaceSupport = true,
						tagSupport = new
						{
							valueSet = s_supportedCompletionItemTags
						},
						resolveSupport = new
						{
							properties = s_resolvableCompletionItemProperties
						}
					},
					// The response converter resolves exactly these list defaults onto items that omit them.
					completionList = new
					{
						itemDefaults = s_supportedCompletionItemDefaults
					}
				},
				hover = new
				{
					contentFormat = s_supportedDocumentationFormats
				},
				definition = new
				{
					linkSupport = true
				},
				documentSymbol = new
				{
					// The provider consumes the hierarchical DocumentSymbol shape, so LuaLS may skip
					// the flat SymbolInformation compatibility form.
					hierarchicalDocumentSymbolSupport = true
				},
				codeAction = new
				{
					dynamicRegistration = false,
					// The provider consumes the literal CodeAction shape with inline edits, so LuaLS
					// may skip the command fallback form, which this client cannot execute.
					codeActionLiteralSupport = new
					{
						codeActionKind = new
						{
							valueSet = s_supportedCodeActionKinds
						}
					},
					isPreferredSupport = true
				},
				references = new
				{
					dynamicRegistration = false
				},
				rename = new
				{
					dynamicRegistration = false,
					prepareSupport = false
				},
				formatting = new
				{
					dynamicRegistration = false
				},
				publishDiagnostics = new
				{
					versionSupport = true
				},
				signatureHelp = new
				{
					signatureInformation = new
					{
						documentationFormat = s_supportedDocumentationFormats,
						parameterInformation = new
						{
							labelOffsetSupport = true
						}
					},
					contextSupport = true
				},
				semanticTokens = new
				{
					requests = new
					{
						range = false,
						// LuaLS registers full semantic-token requests only (a boolean capability, no result ids),
						// so the client does not advertise a delta capability it could never exercise.
						full = true
					},
					tokenTypes = s_supportedSemanticTokenTypes,
					tokenModifiers = s_supportedSemanticTokenModifiers,
					formats = new[] { "relative" },
					multilineTokenSupport = false,
					overlappingTokenSupport = false,
					augmentsSyntaxTokens = true
				}
			}
		};
	}
}
