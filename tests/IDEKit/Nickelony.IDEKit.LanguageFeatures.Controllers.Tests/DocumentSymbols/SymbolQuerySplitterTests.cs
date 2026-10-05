using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class SymbolQuerySplitterTests
{
	[TestMethod]
	[DataRow("", "", "")]
	[DataRow("   ", "", "")]
	[DataRow("util", "util", "")]
	[DataRow("  util  ", "util", "")]
	[DataRow("util core", "util", "core")]
	[DataRow("util   core  src ", "util", "core  src")]
	[DataRow("util\tcore", "util", "core")]
	public void Split_TakesTheFirstWordAsTheNameAndTheRestAsTheContainer(string query, string expectedName, string expectedContainer)
	{
		(string name, string container) = SymbolQuerySplitter.Split(query);

		Assert.AreEqual(expectedName, name);
		Assert.AreEqual(expectedContainer, container);
	}
}
