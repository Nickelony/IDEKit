namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class NegotiatedServerCapabilitiesTests
{
	[TestMethod]
	public void None_AdvertisesNothing()
	{
		NegotiatedServerCapabilities none = NegotiatedServerCapabilities.None;

		Assert.AreEqual(TextDocumentSyncKind.None, none.TextDocumentSyncKind);
		Assert.IsEmpty(none.SemanticTokenTypes);
		Assert.IsEmpty(none.SemanticTokenModifiers);
		Assert.IsFalse(none.SupportsCompletionResolve);
		Assert.IsFalse(none.SupportsDocumentSymbols);
		Assert.IsFalse(none.SupportsWorkspaceSymbols);
		Assert.IsFalse(none.SupportsWorkspaceSymbolResolve);
		Assert.IsFalse(none.SupportsCodeActions);
		Assert.IsFalse(none.SupportsCodeActionResolve);
		Assert.IsEmpty(none.ExecuteCommands);
		Assert.IsFalse(none.SupportsReferences);
		Assert.IsFalse(none.SupportsRename);
		Assert.IsFalse(none.SupportsFormatting);
		Assert.IsFalse(none.SupportsHover);
		Assert.IsFalse(none.SupportsDefinition);
		Assert.IsFalse(none.SupportsSignatureHelp);
		Assert.IsFalse(none.SupportsSemanticTokensFull);
		Assert.IsFalse(none.SupportsPullDiagnostics);
	}

	[TestMethod]
	public void None_IsOneSharedInstance()
	{
		NegotiatedServerCapabilities first = NegotiatedServerCapabilities.None;
		NegotiatedServerCapabilities second = NegotiatedServerCapabilities.None;

		Assert.AreSame(first, second);
	}

	[TestMethod]
	public void Equality_ComparesTheLegendListsByReference()
	{
		IReadOnlyList<string> sharedLegend = ["variable"];

		Assert.AreEqual(
			new NegotiatedServerCapabilities { SemanticTokenTypes = sharedLegend },
			new NegotiatedServerCapabilities { SemanticTokenTypes = sharedLegend }
		);

		Assert.AreNotEqual(
			new NegotiatedServerCapabilities { SemanticTokenTypes = ["variable"] },
			new NegotiatedServerCapabilities { SemanticTokenTypes = ["variable"] }
		);
	}

	[TestMethod]
	public void With_ChangesOneFlagAndKeepsTheRest()
	{
		NegotiatedServerCapabilities hover = NegotiatedServerCapabilities.None with { SupportsHover = true };

		Assert.IsTrue(hover.SupportsHover);
		Assert.IsFalse(hover.SupportsDefinition);
		Assert.AreNotEqual(NegotiatedServerCapabilities.None, hover);
	}
}
