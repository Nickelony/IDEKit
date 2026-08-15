using Nickelony.IDEKit.IntelliSense.SemanticTokens;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Describes the client capabilities advertised to the Roslyn language server for the initialize request.
/// </summary>
/// <remarks>
/// The advertised set is bounded by what the shared provider framework consumes end to end: every capability
/// below maps to a payload the framework parses and surfaces, so a capability-respecting server may use it. It
/// follows the family's shared capability baseline, with the Roslyn-specific additions called out inline.
/// </remarks>
internal static class ClientCapabilitiesFactory
{
	private static readonly string[] s_supportedDocumentationFormats = ["markdown", "plaintext"];

	// The protocol completion tags the parser maps; unknown tag values stay representable and are ignored.
	private static readonly CompletionItemTag[] s_supportedCompletionItemTags = [CompletionItemTag.Deprecated];

	// The completion-item properties the resolved payload merges back onto the unresolved item.
	private static readonly string[] s_resolvableCompletionItemProperties = ["detail", "documentation", "additionalTextEdits"];

	// The completion-list defaults the response converter resolves onto the items that omit them. Every entry maps
	// to a bound payload member, so nothing is advertised that the model cannot carry.
	private static readonly string[] s_supportedCompletionItemDefaults =
	[
		"commitCharacters", "editRange", "insertTextFormat", "data"
	];

	// Built from the shared constants so the advertised legend cannot drift from the token model in
	// Nickelony.IDEKit.IntelliSense.
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
		TextSemanticTokenModifiers.DefaultLibrary
	];

	// The advertised kind set mirrors the fix and refactor kinds the Roslyn language server serves. The provider
	// maps kinds as opaque strings and applies the literal action's edits, so the set bounds which actions the
	// server may return without excluding any family the provider cannot apply.
	private static readonly string[] s_supportedCodeActionKinds =
	[
		"", "quickfix", "refactor", "refactor.extract", "refactor.inline", "refactor.rewrite",
		"source", "source.organizeImports", "source.fixAll"
	];

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
				// create/rename/delete resource operations the shared workspace-edit model cannot represent;
				// the client would have to drop those edits entirely (fail closed). Without the flag the server
				// serves the simple `changes` form, which the parser always accepts.
				didChangeWatchedFiles = new { dynamicRegistration = false },
				// The Roslyn language server sends workspace/semanticTokens/refresh when a configuration change
				// invalidates cached token colors, and workspace/diagnostic/refresh when a project change
				// invalidates the pulled diagnostics; both are advertised so the provider can re-query.
				semanticTokens = new { refreshSupport = true },
				diagnostics = new { refreshSupport = true }
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
					// The provider consumes the hierarchical DocumentSymbol shape, so the server may skip
					// the flat SymbolInformation compatibility form.
					hierarchicalDocumentSymbolSupport = true
				},
				codeAction = new
				{
					dynamicRegistration = false,
					// The provider consumes the literal CodeAction shape with inline edits, so the server
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
				// The Roslyn language server is pull-oriented: it serves diagnostics through
				// textDocument/diagnostic and refreshes them through workspace/diagnostic/refresh. Advertising
				// the pull capability is what makes the server prefer that model, and related-document reports
				// are consumed by the provider, so they are advertised too.
				diagnostic = new
				{
					dynamicRegistration = false,
					relatedDocumentSupport = true
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
						// The client implements full semantic-token requests only (a boolean capability, no
						// result ids), so it does not advertise a delta capability it could never exercise.
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
