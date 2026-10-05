using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class DocumentSymbolKindExtensionsTests
{
	[TestMethod]
	public void ToIconKey_GivesEveryKindItsOwnSymbolKey()
	{
		DocumentSymbolKind[] kinds = Enum.GetValues<DocumentSymbolKind>();
		string[] keys = [.. kinds.Select(kind => kind.ToIconKey())];

		Assert.HasCount(kinds.Length, keys.Distinct(StringComparer.Ordinal));
		Assert.IsTrue(keys.All(key => key.StartsWith("symbol-", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void ToIconKey_UsesTheCodiconNamesOfVsCode()
	{
		Assert.AreEqual("symbol-method", DocumentSymbolKind.Method.ToIconKey());
		Assert.AreEqual("symbol-enum-member", DocumentSymbolKind.EnumMember.ToIconKey());
		Assert.AreEqual("symbol-type-parameter", DocumentSymbolKind.TypeParameter.ToIconKey());
	}

	[TestMethod]
	public void ToIconKey_RejectsAKindThatIsNotDefined()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ((DocumentSymbolKind)0).ToIconKey());
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ((DocumentSymbolKind)99).ToIconKey());
	}

	[TestMethod]
	public void DefaultGroupLabel_NamesEveryKindInThePlural()
	{
		string[] labels = [.. Enum.GetValues<DocumentSymbolKind>().Select(DocumentSymbolQuickPickOptions.DefaultGroupLabel)];

		Assert.HasCount(labels.Length, labels.Distinct(StringComparer.Ordinal));
		Assert.AreEqual("classes", DocumentSymbolQuickPickOptions.DefaultGroupLabel(DocumentSymbolKind.Class));
		Assert.AreEqual("enumeration members", DocumentSymbolQuickPickOptions.DefaultGroupLabel(DocumentSymbolKind.EnumMember));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DocumentSymbolQuickPickOptions.DefaultGroupLabel((DocumentSymbolKind)0));
	}
}
