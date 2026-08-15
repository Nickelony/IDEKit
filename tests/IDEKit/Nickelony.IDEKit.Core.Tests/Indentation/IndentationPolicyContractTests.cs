namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IndentationPolicyContractTests
{
	private sealed class TrailingBracePolicy : IIndentationPolicy
	{
		public string GetDesiredIndentation(in IndentationContext context)
		{
			// The context carries line text only, so the policy derives the previous indentation with
			// the same shared scan the strategy itself uses.
			string previousIndentation = IndentationOperations.GetLeadingWhitespace(context.PreviousLineText);

			return context.UseSmartIndent && context.PreviousLineText.TrimEnd().EndsWith('{')
				? previousIndentation + context.IndentationUnit
				: previousIndentation;
		}
	}

	[TestMethod]
	public void GetDesiredIndentation_SmartIndentEnabled_AddsOneUnit()
	{
		IIndentationPolicy policy = new TrailingBracePolicy();
		var context = new IndentationContext(
			PreviousLineText: "\tif x then {",
			CurrentLineText: string.Empty,
			IndentationUnit: "\t",
			UseSmartIndent: true);

		Assert.AreEqual("\t\t", policy.GetDesiredIndentation(context));
	}

	[TestMethod]
	public void GetDesiredIndentation_SmartIndentDisabled_KeepsBaseIndentation()
	{
		IIndentationPolicy policy = new TrailingBracePolicy();
		var context = new IndentationContext("\tif x then {", string.Empty, "\t", UseSmartIndent: false);

		Assert.AreEqual("\t", policy.GetDesiredIndentation(context));
	}

	[TestMethod]
	public void IndentationContext_Default_CarriesNullTextAndDisabledSmartIndent()
	{
		// A default instance carries null text members even though they are annotated non-nullable;
		// hosts always construct the context from live line text.
		var context = default(IndentationContext);

		Assert.IsNull(context.PreviousLineText);
		Assert.IsNull(context.CurrentLineText);
		Assert.IsNull(context.IndentationUnit);
		Assert.IsFalse(context.UseSmartIndent);
	}
}
