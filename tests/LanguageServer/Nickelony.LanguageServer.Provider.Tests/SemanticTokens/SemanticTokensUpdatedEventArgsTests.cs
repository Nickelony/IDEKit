namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SemanticTokensUpdatedEventArgsTests
{
	private static readonly string s_filePath = Path.Combine(Path.GetTempPath(), "ls-provider-semantic-tokens", "test.test");

	[TestMethod]
	public void Constructor_CopiesTheSuppliedTokenList()
	{
		var originalToken = new SemanticToken(0, 0, 6, "variable", []);
		var replacementToken = new SemanticToken(1, 0, 6, "function", []);
		List<SemanticToken> sourceTokens = [originalToken];

		var eventArgs = new SemanticTokensUpdatedEventArgs(s_filePath, sourceTokens);

		sourceTokens[0] = replacementToken;
		sourceTokens.Add(replacementToken);

		// The payload is an owned snapshot: the source list cannot mutate what subscribers observe.
		Assert.AreEqual(1, eventArgs.SemanticTokens.Count);
		Assert.AreSame(originalToken, eventArgs.SemanticTokens[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<SemanticToken>)eventArgs.SemanticTokens)[0] = replacementToken);
	}

	[TestMethod]
	public void Constructor_EmptySourceList_StaysEmpty()
	{
		var sourceTokens = new List<SemanticToken>();
		var eventArgs = new SemanticTokensUpdatedEventArgs(s_filePath, sourceTokens);

		sourceTokens.Add(new SemanticToken(0, 0, 6, "variable", []));

		Assert.AreEqual(0, eventArgs.SemanticTokens.Count);
	}
}
