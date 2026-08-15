namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditPreparationResultTests
{
	[TestMethod]
	public void Valid_NullEdits_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditPreparationResult.Valid(null!));
	}

	[TestMethod]
	public void Valid_EmptyEdits_IsValidWithoutIssues()
	{
		TextEditPreparationResult result = TextEditPreparationResult.Valid(new PreparedTextEdits([]));

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(0, result.Issues.Count);
	}

	[TestMethod]
	public void Invalid_EmptyIssues_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => TextEditPreparationResult.Invalid([]));
	}

	[TestMethod]
	public void Invalid_WithIssues_IsInvalidWithoutOperations()
	{
		TextEditPreparationResult result = TextEditPreparationResult.Invalid(
			[new TextEditPreparationIssue(TextEditPreparationIssueKind.NullEdit, 0, null, "The edit is null.")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(0, result.Edits.Operations.Count);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.NullEdit, result.Issues[0].Kind);
	}
}
