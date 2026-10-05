#if AVALONIAEDIT
using System.Diagnostics;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Diagnostics;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class SemanticTokensColorizerTests
{
	/// <summary>Applies a large semantic-token set and clears it again.</summary>
	/// <remarks>
	/// This is the deterministic coverage of the large-set path; the allocation and wall-clock budgets live
	/// in the <c>Performance</c> test below, which each binding's default run deselects.
	/// </remarks>
	[TestMethod]
	public void LargeSemanticTokenSet_AppliesAndClears()
	{
		const int lineCount = 400;
		const int tokensPerLine = 10;

		string text = CreateDocument(lineCount);
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer(text, resolver);

		using (window)
		{
			SemanticToken[] tokens = CreateTokens(editor, lineCount, tokensPerLine);
			SemanticToken[] alternateTokens = CreateTokens(editor, lineCount, tokensPerLine, tokenType: SemanticTokenTypes.Parameter);

			Assert.IsTrue(colorizer.ApplyTokens(tokens));
			Assert.IsTrue(colorizer.ApplyTokens(alternateTokens));
			Assert.IsTrue(colorizer.ApplyTokens(tokens));
			Assert.IsTrue(colorizer.ClearTokens());
		}
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public void LargeSemanticTokenSet_AppliesWithinAllocationAndTimeBounds()
	{
		const int lineCount = 400;
		const int tokensPerLine = 10;
		const int iterations = 20;

		string text = CreateDocument(lineCount);
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer(text, resolver);

		using (window)
		{
			SemanticToken[] tokens = CreateTokens(editor, lineCount, tokensPerLine);
			SemanticToken[] alternateTokens = CreateTokens(editor, lineCount, tokensPerLine, tokenType: SemanticTokenTypes.Parameter);

			Assert.IsTrue(colorizer.ApplyTokens(tokens));

			// Re-pushing the unchanged set must stay cheap: the repeat check allocates no per-token structures.
			long repeatAllocated = MeasureAllocations(() => colorizer.ApplyTokens(tokens), iterations);

			Assert.IsLessThan(1024L * 1024L, repeatAllocated, $"Re-pushing {tokens.Length} unchanged semantic tokens allocated {repeatAllocated} B.");

			// Alternate between two distinct sets so every measured push exercises the full application path.
			int pushIndex = 0;
			void pushAlternating() => colorizer.ApplyTokens((pushIndex++ & 1) == 0 ? tokens : alternateTokens);

			long allocated = MeasureAllocations(pushAlternating, iterations);
			double elapsed = MeasureElapsed(pushAlternating, iterations);

			Assert.IsLessThan(256L * 1024L * 1024L, allocated, $"Applying {tokens.Length} semantic tokens allocated {allocated} B.");

			// The time bound is a generous smoke bound instead of a performance budget: it only has to catch a
			// pathologically slow path on a loaded agent, while the allocation budgets carry the real cost
			// assertion.
			Assert.IsLessThan(30_000.0, elapsed, $"Applying {tokens.Length} semantic tokens took {elapsed:F1} ms.");

			Assert.IsTrue(colorizer.ClearTokens());
		}
	}

	private static string CreateDocument(int lineCount) => string.Concat(Enumerable.Repeat("final value = 1\n", lineCount));

	private static SemanticToken[] CreateTokens(TextEditor editor, int lineCount, int tokensPerLine, string tokenType = SemanticTokenTypes.Variable)
	{
		var tokens = new SemanticToken[lineCount * tokensPerLine];
		int index = 0;

		for (int line = 0; line < lineCount; line++)
		{
			for (int token = 0; token < tokensPerLine; token++)
			{
				int offset = editor.Document.GetOffset(new TextLocation(line + 1, (token % 15) + 1));
				tokens[index++] = new SemanticToken(new TextRange(offset, 1), tokenType);
			}
		}

		return tokens;
	}

	private static long MeasureAllocations(Action action, int iterations)
	{
		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int iteration = 0; iteration < iterations; iteration++)
			action();

		return GC.GetAllocatedBytesForCurrentThread() - before;
	}

	private static double MeasureElapsed(Action action, int iterations)
	{
		var stopwatch = Stopwatch.StartNew();

		for (int iteration = 0; iteration < iterations; iteration++)
			action();

		stopwatch.Stop();
		return stopwatch.Elapsed.TotalMilliseconds;
	}
}
