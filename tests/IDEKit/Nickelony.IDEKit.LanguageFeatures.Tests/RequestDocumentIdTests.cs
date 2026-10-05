using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>Pins that every provider request requires a document identifier and stores the one it was given.</summary>
[TestClass]
public sealed class RequestDocumentIdTests
{
	private const string DocumentId = "scripts/main.lua";

	public static IEnumerable<object[]> RequestFactories()
	{
		yield return Factory(static id => new CodeActionRequest(id, "text", new TextRange(0, 1)));
		yield return Factory(static id => new CompletionRequest(id, "text", 1));
		yield return Factory(static id => new DiagnosticsRequest(id, "text"));
		yield return Factory(static id => new DocumentSymbolRequest(id, "text", filterText: string.Empty));
		yield return Factory(static id => new HoverRequest(id, "text", 1));
		yield return Factory(static id => new DefinitionRequest(id, "text", "name"));
		yield return Factory(static id => new SignatureHelpRequest(id, "text", 1));
		yield return Factory(static id => new ReferenceRequest(id, "text", 1));
		yield return Factory(static id => new RenameRequest(documentId: id, documentText: "text", symbolOffset: 1, newName: "renamed"));
		yield return Factory(static id => new FormattingRequest(id, "text", new FormattingOptions(tabSize: 4, insertSpaces: true)));
	}

	[TestMethod]
	[DynamicData(nameof(RequestFactories))]
	public void Constructor_StoresTheDocumentId(Func<string, object> create)
	{
		object request = create(DocumentId);
		Assert.AreEqual(DocumentId, request.GetType().GetProperty("DocumentId")!.GetValue(request));
	}

	[TestMethod]
	[DynamicData(nameof(RequestFactories))]
	public void Constructor_NullDocumentId_ThrowsArgumentNullException(Func<string, object> create) =>
		Assert.ThrowsExactly<ArgumentNullException>(() => create(null!));

	[TestMethod]
	[DynamicData(nameof(RequestFactories))]
	public void Constructor_BlankDocumentId_ThrowsArgumentException(Func<string, object> create)
	{
		Assert.ThrowsExactly<ArgumentException>(() => create(string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => create("   "));
	}

	private static object[] Factory(Func<string, object> create) => [create];
}
