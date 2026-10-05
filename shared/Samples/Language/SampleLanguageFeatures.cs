using Nickelony.IDEKit.JsonSchema;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.LanguageServer.Provider.Abstractions;

namespace Nickelony.Samples;

/// <summary>
/// The language-feature contracts the editor session consumes, whichever implementation answers them; a missing contract leaves its feature off.
/// </summary>
/// <param name="Completion">Answers completion requests.</param>
/// <param name="Hover">Answers hover requests.</param>
/// <param name="SignatureHelp">Answers signature-help requests.</param>
/// <param name="CodeActions">Answers code-action requests.</param>
/// <param name="Diagnostics">Answers diagnostics requests, or <see langword="null"/> when the implementation publishes diagnostics instead.</param>
/// <param name="DiagnosticsSource">Publishes diagnostics, or <see langword="null"/> when the implementation answers requests instead.</param>
/// <param name="SemanticTokens">Publishes semantic tokens.</param>
/// <param name="Formatting">Answers formatting requests.</param>
/// <param name="Rename">Answers rename requests.</param>
/// <param name="Definitions">Answers definition requests.</param>
/// <param name="References">Answers reference requests.</param>
/// <param name="DocumentSymbols">Answers the document-symbol requests of the outline.</param>
internal sealed record SampleLanguageFeatures(
	ICompletionProvider? Completion = null,
	IHoverProvider? Hover = null,
	ISignatureHelpProvider? SignatureHelp = null,
	ICodeActionProvider? CodeActions = null,
	IDiagnosticsProvider? Diagnostics = null,
	IDiagnosticsSource? DiagnosticsSource = null,
	ISemanticTokensSource? SemanticTokens = null,
	IFormattingProvider? Formatting = null,
	IRenameProvider? Rename = null,
	IDefinitionProvider? Definitions = null,
	IReferenceProvider? References = null,
	IDocumentSymbolProvider? DocumentSymbols = null
)
{
	/// <summary>No language features: the text is highlighted and edited, and nothing more.</summary>
	internal static SampleLanguageFeatures None { get; } = new();

	/// <summary>The in-process providers for the Lua-like sample language, which need no language server.</summary>
	internal static SampleLanguageFeatures CreateLua()
	{
		return new SampleLanguageFeatures(
			Completion: new KeywordCompletionProvider(),
			Hover: new KeywordHoverProvider(),
			SignatureHelp: new FunctionSignatureHelpProvider(),
			CodeActions: new TrailingWhitespaceCodeActionProvider(),
			Diagnostics: new LuaDiagnosticsProvider(),
			SemanticTokens: new FunctionSemanticTokensSource(),
			Rename: new IdentifierRenameProvider(),
			Definitions: new DeclarationDefinitionProvider(),
			References: new IdentifierReferenceProvider(),
			DocumentSymbols: new DeclarationDocumentSymbolProvider()
		);
	}

	/// <summary>The providers for the settings file, which answer from the vocabulary of its JSON Schema.</summary>
	internal static SampleLanguageFeatures CreateSettings(JsonSchemaVocabularyIndex vocabulary)
	{
		return new SampleLanguageFeatures(
			Completion: new SchemaCompletionProvider(vocabulary),
			Hover: new SchemaHoverProvider(vocabulary),
			CodeActions: new TrailingWhitespaceCodeActionProvider(),
			Diagnostics: new RegexDiagnosticsProvider()
		);
	}

	/// <summary>The features of a language server provider: one object answers every contract and pushes diagnostics and semantic tokens.</summary>
	internal static SampleLanguageFeatures FromServer(ILanguageServerFeatureProvider server)
	{
		return new SampleLanguageFeatures(
			Completion: server,
			Hover: server,
			SignatureHelp: server,
			CodeActions: server,
			DiagnosticsSource: server,
			SemanticTokens: server,
			Formatting: server,
			Rename: server,
			Definitions: server,
			References: server,
			DocumentSymbols: server
		);
	}
}
