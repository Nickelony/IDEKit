namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the frozen-set construction contract of <see cref="LineDiffResult"/>: the result copies
/// its input into an immutable set and validates the argument, so equality and hash codes are stable
/// over time.
/// </summary>
[TestClass]
public sealed class LineDiffResultTests
{
	[TestMethod]
	public void Create_FreezesTheSourceSet()
	{
		var source = new HashSet<int> { 1, 2 };

		LineDiffResult result = LineDiffResult.Create(source, isApproximate: false);

		source.Add(3);

		Assert.AreEqual(2, result.ChangedLineNumbers.Count);
		Assert.IsTrue(result.ChangedLineNumbers.SetEquals(new[] { 1, 2 }));
	}

	[TestMethod]
	public void Create_NullSet_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => LineDiffResult.Create(null!, isApproximate: false));
	}

	[TestMethod]
	public void Create_EmptySet_IsValid()
	{
		LineDiffResult result = LineDiffResult.Create(new HashSet<int>(), isApproximate: false);

		Assert.AreEqual(0, result.ChangedLineNumbers.Count);
		Assert.IsFalse(result.IsApproximate);
	}

	[TestMethod]
	public void Equality_DiffersWhenOnlyTheApproximationFlagDiffers()
	{
		LineDiffResult exact = LineDiffResult.Create(new HashSet<int> { 1 }, isApproximate: false);
		LineDiffResult approximate = LineDiffResult.Create(new HashSet<int> { 1 }, isApproximate: true);

		Assert.AreNotEqual(exact, approximate);
	}
}
