namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Randomized invariant checks for the comment scanner: the scenario suites pin individual rules,
/// and these seeded sweeps catch state-machine regressions across delimiter, escape, and terminator
/// combinations.
/// </summary>
[TestClass]
public sealed class CommentOperationsPropertyTests
{
	private const int IterationCount = 400;

	[TestMethod]
	public void MaskComments_RandomDelimiterText_PreservesLengthAndTerminators()
	{
		var random = new Random(20260919);
		CommentSyntax syntax = CommentSyntaxFixtures.CStylePlain;
		char[] alphabet = ['a', 'b', ' ', '\t', '/', '*', ';', '{', '}', '\r', '\n'];

		for (int iteration = 0; iteration < IterationCount; iteration++)
		{
			string text = BuildText(random, alphabet, random.Next(0, 40));
			string masked = CommentOperations.MaskComments(text, syntax);

			Assert.AreEqual(text.Length, masked.Length, $"iteration {iteration}: length changed for [{text}]");

			for (int index = 0; index < text.Length; index++)
			{
				bool terminator = text[index] is '\r' or '\n';

				Assert.AreEqual(
					terminator,
					masked[index] is '\r' or '\n',
					$"iteration {iteration}: terminator mismatch at {index} for [{text}]");

				if (terminator)
					Assert.AreEqual(text[index], masked[index], $"iteration {iteration}: terminator changed at {index} for [{text}]");
			}
		}
	}

	[TestMethod]
	public void MaskComments_RandomDelimiterText_LeavesNoComment()
	{
		var random = new Random(20260920);
		CommentSyntax syntax = CommentSyntaxFixtures.CStylePlain;
		char[] alphabet = ['a', 'b', ' ', '/', '*', ';', '\r', '\n'];

		for (int iteration = 0; iteration < IterationCount; iteration++)
		{
			string text = BuildText(random, alphabet, random.Next(0, 40));
			string masked = CommentOperations.MaskComments(text, syntax);

			// Masked characters become spaces, which cannot open a comment, so a full pass must leave
			// no comment behind.
			Assert.IsNull(CommentOperations.FindComment(masked, syntax), $"iteration {iteration}: [{text}] -> [{masked}]");
		}
	}

	[TestMethod]
	public void FindComment_BackslashRunsBeforeQuote_FollowEscapingParity()
	{
		// The quote is escaped exactly when the backslash run is odd, so the trailing line comment is
		// hidden inside the string for even runs and visible for odd runs.
		CommentSyntax syntax = CommentSyntaxFixtures.CStyleDoubleQuoted;

		for (int backslashCount = 0; backslashCount <= 6; backslashCount++)
		{
			string text = new string('\\', backslashCount) + '"' + "//";

			Assert.AreEqual(
				backslashCount % 2 == 1,
				CommentOperations.FindComment(text, syntax) is not null,
				$"backslashCount={backslashCount}");
		}
	}

	private static string BuildText(Random random, char[] alphabet, int length)
	{
		return string.Create(length, (random, alphabet), static (span, state) =>
		{
			for (int index = 0; index < span.Length; index++)
				span[index] = state.alphabet[state.random.Next(state.alphabet.Length)];
		});
	}
}
