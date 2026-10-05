namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditSkipResultTests
{
	[TestMethod]
	public void Success_WithoutSkippedIssues_HasNoSkippedEntries()
	{
		var result = TextEditSkipResult.Success(new PreparedTextEdits([]), []);

		Assert.IsFalse(result.HasSkippedEntries);
		Assert.IsEmpty(result.SkippedIssues);
	}

	[TestMethod]
	public void Success_CopiesTheSkippedIssues()
	{
		var issues = new List<TextEditPreparationIssue>
		{
			new(kind: TextEditPreparationIssueKind.RangeOutOfBounds, editIndex: 0, relatedEditIndex: null, message: "outside"),
		};

		var result = TextEditSkipResult.Success(new PreparedTextEdits([]), issues);
		issues.Clear();

		Assert.IsTrue(result.HasSkippedEntries);
		Assert.HasCount(1, result.SkippedIssues);
	}

	[TestMethod]
	public void Success_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditSkipResult.Success(null!, []));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditSkipResult.Success(new PreparedTextEdits([]), null!));
	}
}
