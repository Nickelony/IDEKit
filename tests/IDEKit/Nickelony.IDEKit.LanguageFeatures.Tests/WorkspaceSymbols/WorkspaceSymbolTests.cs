using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class WorkspaceSymbolTests
{
	private static readonly WorkspaceSymbolLocation s_location = new(
		"C:\\ws\\util.lua",
		new TextPositionRange(new TextPosition(3, 2), new TextPosition(3, 6))
	);

	[TestMethod]
	public void Constructor_StoresTheNameTheKindAndTheLocation()
	{
		var symbol = new WorkspaceSymbol("Util", DocumentSymbolKind.Class, s_location);

		Assert.AreEqual("Util", symbol.Name);
		Assert.AreEqual(DocumentSymbolKind.Class, symbol.Kind);
		Assert.AreSame(s_location, symbol.Location);
		Assert.IsNull(symbol.ContainerName);
		Assert.IsNull(symbol.Data);
	}

	[TestMethod]
	public void Constructor_RejectsABlankNameANullLocationAndAnUndefinedKind()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSymbol(null!, DocumentSymbolKind.Class, s_location));
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceSymbol(" ", DocumentSymbolKind.Class, s_location));
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSymbol("Util", DocumentSymbolKind.Class, null!));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WorkspaceSymbol("Util", default, s_location));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WorkspaceSymbol("Util", (DocumentSymbolKind)99, s_location));
	}

	[TestMethod]
	public void ContainerName_IsTrimmedAndABlankValueCountsAsAbsent()
	{
		var trimmed = new WorkspaceSymbol("Util", DocumentSymbolKind.Method, s_location) { ContainerName = "  Core  " };
		var blank = new WorkspaceSymbol("Util", DocumentSymbolKind.Method, s_location) { ContainerName = "   " };

		Assert.AreEqual("Core", trimmed.ContainerName);
		Assert.IsNull(blank.ContainerName);
	}

	[TestMethod]
	public void Data_CarriesTheProviderPayload()
	{
		object payload = new();
		var symbol = new WorkspaceSymbol("Util", DocumentSymbolKind.Method, s_location) { Data = payload };

		Assert.AreSame(payload, symbol.Data);
	}

	[TestMethod]
	public void Location_KeepsTheRangeOptionalUntilItIsResolved()
	{
		var unresolved = new WorkspaceSymbolLocation("C:\\ws\\util.lua");

		Assert.IsNull(unresolved.Range);
		Assert.AreEqual("C:\\ws\\util.lua", unresolved.DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(3, 2), new TextPosition(3, 6)), s_location.Range);
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSymbolLocation(null!));
	}

	[TestMethod]
	public void Request_KeepsTheQueryAndAcceptsAnEmptyOne()
	{
		Assert.AreEqual("util", new WorkspaceSymbolRequest("util").Query);
		Assert.AreEqual(string.Empty, new WorkspaceSymbolRequest(string.Empty).Query);
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSymbolRequest(null!));
	}
}
