using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DefinitionLocationTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Location_DocumentId_IsTrimmedAndBlankBecomesNull()
	{
		var targetRange = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));

		Assert.AreEqual("scripts/objects.lua", new DefinitionLocation(targetRange, "  scripts/objects.lua  ").DocumentId);
		Assert.IsNull(new DefinitionLocation(targetRange, "   ").DocumentId);
	}

	[TestMethod]
	public void Location_StoresTargetRangeDocumentIdAndSelectionRange()
	{
		var targetRange = new TextPositionRange(new TextPosition(3, 6), new TextPosition(3, 16));
		var selectionRange = new TextPositionRange(new TextPosition(3, 6), new TextPosition(3, 10));

		var location = new DefinitionLocation(targetRange, "scripts/objects.lua", selectionRange);

		Assert.AreEqual(targetRange, location.TargetRange);
		Assert.AreEqual("scripts/objects.lua", location.DocumentId);
		Assert.AreEqual(selectionRange, location.SelectionRange);
	}

	[TestMethod]
	public void Location_DefaultsDocumentIdAndSelectionRangeToNull()
	{
		var targetRange = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));

		var location = new DefinitionLocation(targetRange);

		Assert.AreEqual(targetRange, location.TargetRange);
		Assert.IsNull(location.DocumentId);
		Assert.IsNull(location.SelectionRange);
	}

	[TestMethod]
	public void Location_Equality_ComparesEveryComponent()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(1, 8));
		var selectionRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(1, 5));

		var baseline = new DefinitionLocation(targetRange, "scripts/objects.lua", selectionRange);
		var equal = new DefinitionLocation(targetRange, "scripts/objects.lua", selectionRange);

		Assert.AreEqual(baseline, equal);
		Assert.AreEqual(baseline.GetHashCode(), equal.GetHashCode());

		Assert.AreNotEqual(
			baseline,
			new DefinitionLocation(new TextPositionRange(new TextPosition(2, 0), new TextPosition(2, 4)), "scripts/objects.lua", selectionRange)
		);

		Assert.AreNotEqual(baseline, new DefinitionLocation(targetRange, "scripts/other.lua", selectionRange));
		Assert.AreNotEqual(baseline, new DefinitionLocation(targetRange, null, selectionRange));
		Assert.AreNotEqual(baseline, new DefinitionLocation(targetRange, "scripts/objects.lua", null));
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "WhitespaceOnly")]
	public void Location_BlankDocumentId_IsNormalizedToNull(string documentId)
	{
		var range = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));

		var location = new DefinitionLocation(range, documentId);

		Assert.IsNull(location.DocumentId);
	}

	[TestMethod]
	public void Location_NavigationStart_SelectionRangePresent_UsesTheSelectionStart()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(3, 4));
		var selectionRange = new TextPositionRange(new TextPosition(2, 5), new TextPosition(2, 9));
		var location = new DefinitionLocation(targetRange, selectionRange: selectionRange);

		Assert.AreEqual(new TextPosition(2, 5), location.NavigationStart);
	}

	[TestMethod]
	public void Location_NavigationStart_SelectionRangeAbsent_UsesTheTargetStart()
	{
		var targetRange = new TextPositionRange(new TextPosition(1, 2), new TextPosition(3, 4));
		var location = new DefinitionLocation(targetRange);

		Assert.AreEqual(new TextPosition(1, 2), location.NavigationStart);
	}
}
