using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class ParameterInformationTests
{
	[TestMethod]
	public void ParameterInfo_StoresLabelAndOptionalDocumentation()
	{
		var plain = new ParameterInformation("value");

		Assert.AreEqual("value", plain.Label);
		Assert.IsNull(plain.Documentation);

		var documented = new ParameterInformation("value", "The value to print.");

		Assert.AreEqual("The value to print.", documented.Documentation);
	}

	[TestMethod]
	public void ParameterInfo_WhitespaceDocumentation_IsNormalizedToAbsent()
	{
		var parameter = new ParameterInformation("value", "   ");
		Assert.IsNull(parameter.Documentation);
	}

	[TestMethod]
	public void ParameterInfo_PaddedDocumentation_IsTrimmed()
	{
		var parameter = new ParameterInformation("value", "  The value to print.  ");
		Assert.AreEqual("The value to print.", parameter.Documentation);
	}
}
