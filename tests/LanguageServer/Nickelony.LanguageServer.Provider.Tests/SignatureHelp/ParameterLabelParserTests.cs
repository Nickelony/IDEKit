using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ParameterLabelParserTests
{
	[TestMethod]
	public void TryParse_OffsetPair_ReturnsTheLabelSubstring()
	{
		bool succeeded = ParameterLabelParser.TryParse(
			signatureLabel: "fn(first, second)",
			parameterLabelPayload: SignatureHelpParameterLabelPayload.FromOffsets(3, 8),
			out string? parameterLabel,
			startIndex: out _,
			endIndex: out _
		);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("first", parameterLabel);
	}

	[TestMethod]
	public void TryParse_OffsetPair_ReturnsTheClampedRange()
	{
		bool succeeded = ParameterLabelParser.TryParse(
			signatureLabel: "fn(first, second)",
			parameterLabelPayload: SignatureHelpParameterLabelPayload.FromOffsets(3, 8),
			out string? parameterLabel,
			out int startIndex,
			out int endIndex
		);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("first", parameterLabel);
		Assert.AreEqual(3, startIndex);
		Assert.AreEqual(8, endIndex);
	}

	[TestMethod]
	public void TryParse_OffsetsBeyondTheLabel_ClampsTheReportedRange()
	{
		bool succeeded = ParameterLabelParser.TryParse(
			signatureLabel: "abc",
			parameterLabelPayload: SignatureHelpParameterLabelPayload.FromOffsets(1, 99),
			out string? parameterLabel,
			out int startIndex,
			out int endIndex
		);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("bc", parameterLabel);
		Assert.AreEqual(1, startIndex);
		Assert.AreEqual(3, endIndex);
	}

	[TestMethod]
	public void TryParse_Failure_ReportsAnEmptyRange()
	{
		Assert.IsFalse(
			ParameterLabelParser.TryParse(
				signatureLabel: "fn(abc)",
				parameterLabelPayload: SignatureHelpParameterLabelPayload.FromOffsets(5, 2),
				parameterLabel: out _,
				out int startIndex,
				out int endIndex
			)
		);

		Assert.AreEqual(0, startIndex);
		Assert.AreEqual(0, endIndex);
	}

	[TestMethod]
	public void TryParse_NegativeStart_ClampsToZero()
	{
		bool succeeded = ParameterLabelParser.TryParse(
			signatureLabel: "abc",
			parameterLabelPayload: SignatureHelpParameterLabelPayload.FromOffsets(-5, 2),
			out string? parameterLabel,
			startIndex: out _,
			endIndex: out _
		);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("ab", parameterLabel);
	}

	[TestMethod]
	public void TryParse_InvertedOrEmptyRange_ReturnsFalse()
	{
		Assert.IsFalse(TryExtract("fn(abc)", SignatureHelpParameterLabelPayload.FromOffsets(5, 2)));
		Assert.IsFalse(TryExtract("fn(abc)", SignatureHelpParameterLabelPayload.FromOffsets(2, 2)));
	}

	[TestMethod]
	public void TryParse_TextOrEmptyParameterLabel_ReturnsFalse()
	{
		Assert.IsFalse(TryExtract("fn(abc)", SignatureHelpParameterLabelPayload.FromText("abc")));
		Assert.IsFalse(TryExtract("fn(abc)", default));
	}

	[TestMethod]
	public void TryParse_EmptySignatureLabel_ReturnsFalse()
	{
		Assert.IsFalse(TryExtract(string.Empty, SignatureHelpParameterLabelPayload.FromOffsets(0, 1)));
		Assert.IsFalse(TryExtract("abc", default));
	}

	private static bool TryExtract(string signatureLabel, SignatureHelpParameterLabelPayload parameterLabelPayload) =>
		ParameterLabelParser.TryParse(signatureLabel, parameterLabelPayload, parameterLabel: out _, startIndex: out _, endIndex: out _);
}
