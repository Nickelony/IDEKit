namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class WindowMessageParamsTests
{
	[TestMethod]
	public void Deserialize_TypeAndMessage_ReadsBoth()
	{
		WindowMessageParams parameters = WireJsonTestHelpers.Deserialize<WindowMessageParams>("""{ "type": 2, "message": "careful" }""");

		Assert.AreEqual(MessageType.Warning, parameters.Type);
		Assert.AreEqual("careful", parameters.Message);
	}

	[TestMethod]
	public void Deserialize_WithoutTypeOrMessage_LeavesBothNull()
	{
		WindowMessageParams parameters = WireJsonTestHelpers.Deserialize<WindowMessageParams>("{ }");

		Assert.IsNull(parameters.Type);
		Assert.IsNull(parameters.Message);
	}

	[TestMethod]
	public void Deserialize_UnknownType_KeepsTheNumericValueSoTheTargetTreatsItAsALog()
	{
		WindowMessageParams parameters = WireJsonTestHelpers.Deserialize<WindowMessageParams>("""{ "type": 99, "message": "x" }""");
		Assert.AreEqual((MessageType)99, parameters.Type);
	}
}
