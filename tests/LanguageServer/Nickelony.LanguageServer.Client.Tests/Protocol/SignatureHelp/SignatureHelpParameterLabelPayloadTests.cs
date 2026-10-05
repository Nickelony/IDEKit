using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SignatureHelpParameterLabelPayloadTests
{
	[TestMethod]
	public void Deserialize_String_ReadsTheTextForm()
	{
		SignatureHelpParameterLabelPayload label = JsonSerializer.Deserialize<SignatureHelpParameterLabelPayload>("\"value\"");

		Assert.AreEqual("value", label.Text);
		Assert.IsNull(label.StartOffset);
		Assert.IsNull(label.EndOffset);
	}

	[TestMethod]
	public void Deserialize_OffsetPair_ReadsTheOffsetForm()
	{
		SignatureHelpParameterLabelPayload label = JsonSerializer.Deserialize<SignatureHelpParameterLabelPayload>("[3, 8]");

		Assert.IsNull(label.Text);
		Assert.AreEqual(3, label.StartOffset);
		Assert.AreEqual(8, label.EndOffset);
	}

	[TestMethod]
	public void Deserialize_OffsetPairWithOutOfRangeValues_KeepsTheServerValues()
	{
		SignatureHelpParameterLabelPayload label = JsonSerializer.Deserialize<SignatureHelpParameterLabelPayload>("[-5, 99]");
		Assert.AreEqual(SignatureHelpParameterLabelPayload.FromOffsets(-5, 99), label);
	}

	[TestMethod]
	public void Deserialize_ArraysThatAreNotAnOffsetPair_ReadAsTheEmptyLabel()
	{
		foreach (string json in new[] { "[2]", "[]", "[\"a\", \"b\"]", "[0, 1, 2]", "[1.5, 2]", "[0, null]" })
			Assert.AreEqual(default, JsonSerializer.Deserialize<SignatureHelpParameterLabelPayload>(json), json);
	}

	[TestMethod]
	public void Deserialize_OtherKinds_ReadAsTheEmptyLabel()
	{
		foreach (string json in new[] { "42", "true", "null", "{ \"a\": [ 1 ] }" })
			Assert.AreEqual(default, JsonSerializer.Deserialize<SignatureHelpParameterLabelPayload>(json), json);
	}

	[TestMethod]
	public void Serialize_WritesEachFormInItsOriginalShape()
	{
		Assert.AreEqual("\"value\"", JsonSerializer.Serialize(SignatureHelpParameterLabelPayload.FromText("value")));
		Assert.AreEqual("[3,8]", JsonSerializer.Serialize(SignatureHelpParameterLabelPayload.FromOffsets(3, 8)));
		Assert.AreEqual("null", JsonSerializer.Serialize(default(SignatureHelpParameterLabelPayload)));
	}

	[TestMethod]
	public void Serialize_ParameterWithTheEmptyLabel_OmitsTheLabelMember()
	{
		string json = JsonSerializer.Serialize(new ParameterInformationPayload());
		Assert.AreEqual("{}", json);
	}

	[TestMethod]
	public void FromText_NullText_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => SignatureHelpParameterLabelPayload.FromText(null!));
}
