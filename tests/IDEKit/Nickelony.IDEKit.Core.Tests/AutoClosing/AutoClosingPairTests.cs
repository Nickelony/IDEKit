namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the construction invariants of <see cref="AutoClosingPair"/>: a pair always carries non-<see langword="null"/> opening and closing text,
/// whichever construction path sets them.
/// </summary>
[TestClass]
public sealed class AutoClosingPairTests
{
	[TestMethod]
	public void Constructor_NullOpen_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new AutoClosingPair(null!, ")"));

	[TestMethod]
	public void Constructor_NullClose_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new AutoClosingPair("(", null!));

	[TestMethod]
	public void ObjectInitializer_NullToken_ThrowsArgumentNullException()
	{
		// The init accessors validate too, so a pair cannot lose its invariant through a
		// with-expression or an object initializer.
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = AutoClosingPair.Parentheses with { Open = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = AutoClosingPair.Parentheses with { Close = null! });
	}

	[TestMethod]
	public void Constructor_EmptyTokens_ArePreserved()
	{
		// An empty token is a configuration that the resolver ignores, not a construction error; the
		// constructor only enforces the null contract.
		var pair = new AutoClosingPair(string.Empty, string.Empty);

		Assert.AreEqual(string.Empty, pair.Open);
		Assert.AreEqual(string.Empty, pair.Close);
	}
}
