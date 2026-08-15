namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class RequestTokenSourceTests
{
	[TestMethod]
	public void BeginRequest_ReturnsCurrentToken()
	{
		var source = new RequestTokenSource();

		long token = source.BeginRequest();

		Assert.IsTrue(source.IsCurrent(token));
	}

	[TestMethod]
	public void BeginRequest_SupersedesEarlierToken()
	{
		var source = new RequestTokenSource();

		long first = source.BeginRequest();
		long second = source.BeginRequest();

		Assert.IsFalse(source.IsCurrent(first));
		Assert.IsTrue(source.IsCurrent(second));
	}

	[TestMethod]
	public void Invalidate_SupersedesOutstandingToken()
	{
		var source = new RequestTokenSource();

		long token = source.BeginRequest();
		source.Invalidate();

		Assert.IsFalse(source.IsCurrent(token));
	}

	[TestMethod]
	public void BeginRequest_AfterInvalidate_ReturnsCurrentToken()
	{
		var source = new RequestTokenSource();

		long token = source.BeginRequest();
		source.Invalidate();
		long newToken = source.BeginRequest();

		Assert.IsFalse(source.IsCurrent(token));
		Assert.IsTrue(source.IsCurrent(newToken));
	}

	[TestMethod]
	public void IsCurrent_DefaultToken_IsNeverCurrent()
	{
		var source = new RequestTokenSource();

		Assert.IsFalse(source.IsCurrent(0));

		long token = source.BeginRequest();

		Assert.IsTrue(source.IsCurrent(token));
		Assert.IsFalse(source.IsCurrent(0));
	}

	[TestMethod]
	public void BeginRequest_ConcurrentCallers_KeepExactlyOneCurrentToken()
	{
		var source = new RequestTokenSource();
		var tokens = new long[16];

		Parallel.For(0, tokens.Length, index => tokens[index] = source.BeginRequest());

		Assert.AreEqual(1, tokens.Count(token => source.IsCurrent(token)));
		Assert.IsTrue(source.IsCurrent(tokens.Max()));
	}
}
