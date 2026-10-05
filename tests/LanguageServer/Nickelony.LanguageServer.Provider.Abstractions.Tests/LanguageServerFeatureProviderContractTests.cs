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
using Nickelony.IDEKit.LanguageFeatures.Synchronization;

namespace Nickelony.LanguageServer.Provider.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerFeatureProviderContractTests
{
	public static IEnumerable<object[]> InheritedContracts()
	{
		return
		[
			[typeof(ICompletionProvider)],
			[typeof(IHoverProvider)],
			[typeof(IDefinitionProvider)],
			[typeof(ISignatureHelpProvider)],
			[typeof(ICodeActionProvider)],
			[typeof(IDocumentSymbolProvider)],
			[typeof(IReferenceProvider)],
			[typeof(IRenameProvider)],
			[typeof(IFormattingProvider)],
			[typeof(IDiagnosticsSource)],
			[typeof(ISemanticTokensSource)],
			[typeof(ILanguageServerCapabilitiesSource)],
			[typeof(IDocumentSynchronizationTarget)],
		];
	}

	[TestMethod]
	[DynamicData(nameof(InheritedContracts))]
	public void Aggregate_IsEachLanguageFeaturesContractAndSource(Type contract) =>
		Assert.IsTrue(contract.IsAssignableFrom(typeof(ILanguageServerFeatureProvider)), $"The aggregate must be {contract.Name}.");

	[TestMethod]
	public void Aggregate_DoesNotImplementTheDiagnosticsRequestContract()
	{
		Assert.IsFalse(
			typeof(IDiagnosticsProvider).IsAssignableFrom(typeof(ILanguageServerFeatureProvider)),
			"The source is the aggregate's one diagnostics surface."
		);
	}

	// A compile-time check of the same assignability: each line stops compiling when the aggregate stops being the contract.
	private static void AcceptEveryContract(ILanguageServerFeatureProvider provider)
	{
		ICompletionProvider completion = provider;
		IHoverProvider hover = provider;
		IDefinitionProvider definition = provider;
		ISignatureHelpProvider signatureHelp = provider;
		ICodeActionProvider codeActions = provider;
		IDocumentSymbolProvider documentSymbols = provider;
		IReferenceProvider references = provider;
		IRenameProvider rename = provider;
		IFormattingProvider formatting = provider;
		IDiagnosticsSource diagnostics = provider;
		ISemanticTokensSource semanticTokens = provider;
		ILanguageServerCapabilitiesSource capabilities = provider;

		_ = (
			completion,
			hover,
			definition,
			signatureHelp,
			codeActions,
			documentSymbols,
			references,
			rename,
			formatting,
			diagnostics,
			semanticTokens,
			capabilities
		);
	}
}
