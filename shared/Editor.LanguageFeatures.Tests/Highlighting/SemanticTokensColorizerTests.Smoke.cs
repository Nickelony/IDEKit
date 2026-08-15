#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Diagnostics;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Diagnostics;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class SemanticTokensColorizerTests
{
	/// <summary>
	/// Applies a large semantic-token set and clears it again. This is the deterministic coverage of the
	/// large-set path; the allocation and wall-clock budgets live in the <c>Performance</c> test below,
	/// which the default run deselects.
	/// </summary>
	[TestMethod]
	public void LargeSemanticTokenSet_AppliesAndClears()
	{
		const int lineCount = 400;
		const int tokensPerLine = 10;
		string text = CreateDocument(lineCount);
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer(text, resolver);

		using (window)
		{
			TextSemanticToken[] tokens = CreateTokens(editor, lineCount, tokensPerLine);
			TextSemanticToken[] alternateTokens = CreateTokens(editor, lineCount, tokensPerLine, TextSemanticTokenTypes.Parameter);

			Assert.IsTrue(colorizer.SetTokens(tokens));
			Assert.IsTrue(colorizer.SetTokens(alternateTokens));
			Assert.IsTrue(colorizer.SetTokens(tokens));
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
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer(text, resolver);

		using (window)
		{
			TextSemanticToken[] tokens = CreateTokens(editor, lineCount, tokensPerLine);
			TextSemanticToken[] alternateTokens = CreateTokens(editor, lineCount, tokensPerLine, TextSemanticTokenTypes.Parameter);

			Assert.IsTrue(colorizer.SetTokens(tokens));

			// Re-pushing the unchanged set must stay cheap: the repeat check allocates no per-token structures.
			long repeatAllocated = MeasureAllocations(() => colorizer.SetTokens(tokens), iterations);

			Assert.IsTrue(repeatAllocated < 1024L * 1024L,
				$"Re-pushing {tokens.Length} unchanged semantic tokens allocated {repeatAllocated} B.");

			// Alternate between two distinct sets so every measured push exercises the full application path.
			int pushIndex = 0;
			Action pushAlternating = () => colorizer.SetTokens((pushIndex++ & 1) == 0 ? tokens : alternateTokens);

			long allocated = MeasureAllocations(pushAlternating, iterations);
			double elapsed = MeasureElapsed(pushAlternating, iterations);

			Assert.IsTrue(allocated < 256L * 1024L * 1024L,
				$"Applying {tokens.Length} semantic tokens allocated {allocated} B.");
			// The time bound is a generous smoke bound instead of a performance budget: it only has to catch a
			// pathologically slow path on a loaded agent, while the allocation budgets carry the real cost
			// assertion.
			Assert.IsTrue(elapsed < 30_000.0,
				$"Applying {tokens.Length} semantic tokens took {elapsed:F1} ms.");

			Assert.IsTrue(colorizer.ClearTokens());
		}
	}

	private static string CreateDocument(int lineCount)
		=> string.Concat(Enumerable.Repeat("final value = 1\n", lineCount));

	private static TextSemanticToken[] CreateTokens(
		TextEditor editor,
		int lineCount,
		int tokensPerLine,
		string tokenType = TextSemanticTokenTypes.Variable)
	{
		var tokens = new TextSemanticToken[lineCount * tokensPerLine];
		int index = 0;

		for (int line = 0; line < lineCount; line++)
		{
			for (int token = 0; token < tokensPerLine; token++)
			{
				int offset = editor.Document.GetOffset(new TextLocation(line + 1, (token % 15) + 1));
				tokens[index++] = new TextSemanticToken(new TextRange(offset, 1), tokenType);
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
