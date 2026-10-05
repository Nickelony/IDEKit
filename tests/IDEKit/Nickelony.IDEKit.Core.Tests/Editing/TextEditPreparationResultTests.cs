namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditPreparationResultTests
{
	[TestMethod]
	public void Success_NullEdits_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => TextEditPreparationResult.Success(null!));

	[TestMethod]
	public void Success_EmptyEdits_SucceedsWithoutIssues()
	{
		var result = TextEditPreparationResult.Success(new PreparedTextEdits([]));

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Issues);
	}

	[TestMethod]
	public void Failure_EmptyIssues_Throws() => Assert.ThrowsExactly<ArgumentException>(() => TextEditPreparationResult.Failure([]));

	[TestMethod]
	public void Failure_WithIssues_FailsWithoutOperations()
	{
		var result = TextEditPreparationResult.Failure([
			new TextEditPreparationIssue(
				kind: TextEditPreparationIssueKind.NullEdit,
				editIndex: 0,
				relatedEditIndex: null,
				message: "The edit is null."
			),
		]);

		Assert.IsFalse(result.Succeeded);
		Assert.IsEmpty(result.Edits.Operations);
		Assert.HasCount(1, result.Issues);
		Assert.AreEqual(TextEditPreparationIssueKind.NullEdit, result.Issues[0].Kind);
	}
}
