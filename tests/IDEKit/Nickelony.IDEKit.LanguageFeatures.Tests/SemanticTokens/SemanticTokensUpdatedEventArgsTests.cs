using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class SemanticTokensUpdatedEventArgsTests
{
	[TestMethod]
	public void Constructor_NullElement_ThrowsArgumentException() =>
		Assert.ThrowsExactly<ArgumentException>(() => new SemanticTokensUpdatedEventArgs("a.lua", [null!]));

	private static readonly string s_documentId = Path.Combine(Path.GetTempPath(), "ls-language-features-semantic-tokens", "test.test");

	[TestMethod]
	public void Constructor_CopiesTheSuppliedTokenList()
	{
		var originalToken = new SemanticToken(new TextRange(0, 6), "variable");
		var replacementToken = new SemanticToken(new TextRange(7, 6), "function");

		List<SemanticToken> sourceTokens = [originalToken];

		var eventArgs = new SemanticTokensUpdatedEventArgs(s_documentId, sourceTokens);

		sourceTokens[0] = replacementToken;
		sourceTokens.Add(replacementToken);

		// The payload is an owned snapshot: the source list cannot mutate what subscribers observe.
		Assert.HasCount(1, eventArgs.SemanticTokens);
		Assert.AreSame(originalToken, eventArgs.SemanticTokens[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<SemanticToken>)eventArgs.SemanticTokens)[0] = replacementToken);
	}

	[TestMethod]
	public void Constructor_EmptySourceList_StaysEmpty()
	{
		var sourceTokens = new List<SemanticToken>();
		var eventArgs = new SemanticTokensUpdatedEventArgs(s_documentId, sourceTokens);

		sourceTokens.Add(new SemanticToken(new TextRange(0, 6), "variable"));

		Assert.IsEmpty(eventArgs.SemanticTokens);
	}
}
