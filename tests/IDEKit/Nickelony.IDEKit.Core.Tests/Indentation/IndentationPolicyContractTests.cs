namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IndentationPolicyContractTests
{
	private sealed class TrailingBracePolicy : IIndentationPolicy
	{
		public string GetDesiredIndentation(in IndentationRequest request)
		{
			// The request carries line text only, so the policy derives the previous indentation with
			// the same shared scan the strategy itself uses.
			string previousIndentation = LineTextOperations.GetLeadingWhitespace(request.PreviousLineText);

			return request.UseSmartIndent && request.PreviousLineText.TrimEnd().EndsWith('{')
				? previousIndentation + request.IndentationUnit
				: previousIndentation;
		}
	}

	[TestMethod]
	public void GetDesiredIndentation_SmartIndentEnabled_AddsOneUnit()
	{
		IIndentationPolicy policy = new TrailingBracePolicy();

		var request = new IndentationRequest(
			PreviousLineText: "\tif x then {",
			CurrentLineText: string.Empty,
			IndentationUnit: "\t",
			UseSmartIndent: true
		);

		Assert.AreEqual("\t\t", policy.GetDesiredIndentation(request));
	}

	[TestMethod]
	public void GetDesiredIndentation_SmartIndentDisabled_KeepsBaseIndentation()
	{
		IIndentationPolicy policy = new TrailingBracePolicy();

		var request = new IndentationRequest(
			PreviousLineText: "\tif x then {",
			CurrentLineText: string.Empty,
			IndentationUnit: "\t",
			UseSmartIndent: false
		);

		Assert.AreEqual("\t", policy.GetDesiredIndentation(request));
	}

	[TestMethod]
	public void IndentationRequest_Default_CarriesNullTextAndDisabledSmartIndent()
	{
		// A default instance carries null text members even though they are annotated non-nullable;
		// hosts always construct the request from live line text.
		var request = default(IndentationRequest);

		Assert.IsNull(request.PreviousLineText);
		Assert.IsNull(request.CurrentLineText);
		Assert.IsNull(request.IndentationUnit);
		Assert.IsFalse(request.UseSmartIndent);
	}
}
