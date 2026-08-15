using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Tests.TestSupport;

namespace Nickelony.IDEKit.IntelliSense.Tests.Navigation;

[TestClass]
public sealed class TextDefinitionTests
{
	[TestMethod]
	public void Request_StoresSnapshotSymbolAndDiscriminator()
	{
		var discriminator = new TestDiscriminator("section");
		var request = new TextDefinitionRequest("document", "objectName", discriminator);

		Assert.AreEqual("document", request.DocumentText);
		Assert.AreEqual("objectName", request.SymbolName);
		Assert.AreSame(discriminator, request.Discriminator);
	}

	[TestMethod]
	public void Request_WithoutDiscriminator_LeavesDiscriminatorNull()
	{
		var request = new TextDefinitionRequest("document", "objectName");

		Assert.IsNull(request.Discriminator);
	}

	[TestMethod]
	public void Request_BlankSymbolName_IsCanonicalizedToTheNoSymbolState()
	{
		var request = new TextDefinitionRequest("document", "   ");

		Assert.AreEqual(string.Empty, request.SymbolName);
	}

	[TestMethod]
	public void Request_PaddedSymbolName_IsTrimmed()
	{
		var request = new TextDefinitionRequest("document", "  objectName  ");

		Assert.AreEqual("objectName", request.SymbolName);
	}

	[TestMethod]
	public void Request_Equality_ComparesEveryComponent()
	{
		var request = new TextDefinitionRequest("document", "objectName");

		Assert.AreEqual(request, new TextDefinitionRequest("document", "objectName"));
		Assert.AreNotEqual(request, new TextDefinitionRequest("document", "otherName"));
		Assert.AreNotEqual(request, new TextDefinitionRequest("other", "objectName"));
	}

	[TestMethod]
	public void Request_ValueEqualDiscriminators_CompareEqual()
	{
		var request = new TextDefinitionRequest("document", "objectName", new TestDiscriminator("section"));
		var equal = new TextDefinitionRequest("document", "objectName", new TestDiscriminator("section"));

		Assert.AreEqual(request, equal);
		Assert.AreNotEqual(request, new TextDefinitionRequest("document", "objectName", new TestDiscriminator("other")));
	}

	[TestMethod]
	public void Location_DocumentId_IsTrimmedAndBlankBecomesNull()
	{
		var targetRange = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));

		Assert.AreEqual("scripts/objects.lua", new TextDefinitionLocation(targetRange, "  scripts/objects.lua  ").DocumentId);
		Assert.IsNull(new TextDefinitionLocation(targetRange, "   ").DocumentId);
	}

	[TestMethod]
	public void Location_StoresTargetRangeDocumentIdAndSelectionRange()
	{
		var targetRange = new TextPositionRange(new TextPosition(3, 6), new TextPosition(3, 16));
		var selectionRange = new TextPositionRange(new TextPosition(3, 6), new TextPosition(3, 10));

		var location = new TextDefinitionLocation(targetRange, "scripts/objects.lua", selectionRange);

		Assert.AreEqual(targetRange, location.TargetRange);
		Assert.AreEqual("scripts/objects.lua", location.DocumentId);
		Assert.AreEqual(selectionRange, location.SelectionRange);
	}

	[TestMethod]
	public void Location_DefaultsDocumentIdAndSelectionRangeToNull()
	{
		var targetRange = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));

		var location = new TextDefinitionLocation(targetRange);

		Assert.AreEqual(targetRange, location.TargetRange);
		Assert.IsNull(location.DocumentId);
		Assert.IsNull(location.SelectionRange);
	}

	[TestMethod]
	public void Location_Equality_ComparesEveryComponent()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(1, 8));
		var selectionRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(1, 5));

		var baseline = new TextDefinitionLocation(targetRange, "scripts/objects.lua", selectionRange);
		var equal = new TextDefinitionLocation(targetRange, "scripts/objects.lua", selectionRange);

		Assert.AreEqual(baseline, equal);
		Assert.AreEqual(baseline.GetHashCode(), equal.GetHashCode());

		Assert.AreNotEqual(baseline, new TextDefinitionLocation(
			new TextPositionRange(new TextPosition(2, 0), new TextPosition(2, 4)),
			"scripts/objects.lua",
			selectionRange));
		Assert.AreNotEqual(baseline, new TextDefinitionLocation(targetRange, "scripts/other.lua", selectionRange));
		Assert.AreNotEqual(baseline, new TextDefinitionLocation(targetRange, null, selectionRange));
		Assert.AreNotEqual(baseline, new TextDefinitionLocation(targetRange, "scripts/objects.lua", null));
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "WhitespaceOnly")]
	public void Location_BlankDocumentId_IsNormalizedToNull(string documentId)
	{
		var range = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));

		var location = new TextDefinitionLocation(range, documentId);

		Assert.IsNull(location.DocumentId);
	}

	[TestMethod]
	public void Location_NavigationStart_SelectionRangePresent_UsesTheSelectionStart()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(3, 4));
		var selectionRange = new TextPositionRange(new TextPosition(2, 5), new TextPosition(2, 9));
		var location = new TextDefinitionLocation(targetRange, selectionRange: selectionRange);

		Assert.AreEqual(new TextPosition(2, 5), location.NavigationStart);
	}

	[TestMethod]
	public void Location_NavigationStart_SelectionRangeAbsent_UsesTheTargetStart()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(3, 4));
		var location = new TextDefinitionLocation(targetRange);

		Assert.AreEqual(new TextPosition(1, 2), location.NavigationStart);
	}
}
