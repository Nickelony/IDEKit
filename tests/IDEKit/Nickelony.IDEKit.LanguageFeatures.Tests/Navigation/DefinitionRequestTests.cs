using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DefinitionRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Request_StoresSnapshotSymbolAndDiscriminator()
	{
		var discriminator = new TestDiscriminator("section");
		var request = new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", discriminator);

		Assert.AreEqual("document", request.DocumentText);
		Assert.AreEqual("objectName", request.SymbolName);
		Assert.AreSame(discriminator, request.Discriminator);
	}

	[TestMethod]
	public void Request_SymbolOffset_IsOptionalAndStored()
	{
		Assert.IsNull(new DefinitionRequest(DocumentId, "document", "objectName").SymbolOffset);
		Assert.AreEqual(4, new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", symbolOffset: 4).SymbolOffset);
		Assert.AreEqual(8, new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", symbolOffset: 8).SymbolOffset);
	}

	[TestMethod]
	public void Request_SymbolOffsetOutsideTheDocument_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", symbolOffset: 9)
		);

		Assert.AreEqual("symbolOffset", exception.ParamName);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", symbolOffset: -1)
		);
	}

	[TestMethod]
	public void Request_Equality_ComparesTheSymbolOffset()
	{
		Assert.AreNotEqual(
			new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", symbolOffset: 1),
			new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", symbolOffset: 2)
		);
	}

	[TestMethod]
	public void Request_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new DefinitionRequest(DocumentId, null!, "objectName"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new DefinitionRequest(DocumentId, "document", null!));
	}

	[TestMethod]
	public void Request_WithoutDiscriminator_LeavesDiscriminatorNull()
	{
		var request = new DefinitionRequest(DocumentId, "document", "objectName");
		Assert.IsNull(request.Discriminator);
	}

	[TestMethod]
	public void Request_BlankSymbolName_IsCanonicalizedToTheNoSymbolState()
	{
		var request = new DefinitionRequest(DocumentId, "document", "   ");
		Assert.AreEqual(string.Empty, request.SymbolName);
	}

	[TestMethod]
	public void Request_PaddedSymbolName_IsTrimmed()
	{
		var request = new DefinitionRequest(DocumentId, "document", "  objectName  ");
		Assert.AreEqual("objectName", request.SymbolName);
	}

	[TestMethod]
	public void Request_Equality_ComparesEveryComponent()
	{
		var request = new DefinitionRequest(DocumentId, "document", "objectName");

		Assert.AreEqual(request, new DefinitionRequest(DocumentId, "document", "objectName"));
		Assert.AreNotEqual(request, new DefinitionRequest(DocumentId, "document", "otherName"));
		Assert.AreNotEqual(request, new DefinitionRequest(DocumentId, "other", "objectName"));
	}

	[TestMethod]
	public void Request_ValueEqualDiscriminators_CompareEqual()
	{
		var request = new DefinitionRequest(
			DocumentId,
			documentText: "document",
			symbolName: "objectName",
			discriminator: new TestDiscriminator("section")
		);

		var equal = new DefinitionRequest(
			DocumentId,
			documentText: "document",
			symbolName: "objectName",
			discriminator: new TestDiscriminator("section")
		);

		Assert.AreEqual(request, equal);

		Assert.AreNotEqual(
			request,
			new DefinitionRequest(DocumentId, documentText: "document", symbolName: "objectName", discriminator: new TestDiscriminator("other"))
		);
	}
}
