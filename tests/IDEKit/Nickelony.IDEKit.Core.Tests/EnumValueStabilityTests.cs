using static Nickelony.IDEKit.Core.Tests.EnumValueAssert;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class EnumValueStabilityTests
{
	[TestMethod]
	public void CommentVocabularyValues_AreStable()
	{
		AssertPinnedValues(
			(CommentKind.Line, 0),
			(CommentKind.Block, 1));

		AssertPinnedValues(
			(StringLiteralStyle.None, 0),
			(StringLiteralStyle.DoubleQuoted, 1),
			(StringLiteralStyle.SingleQuoted, 2),
			(StringLiteralStyle.BacktickQuoted, 4),
			(StringLiteralStyle.TripleDoubleQuoted, 8),
			(StringLiteralStyle.TripleSingleQuoted, 16),
			(StringLiteralStyle.LongBracketQuoted, 32),
			(StringLiteralStyle.VerbatimDoubleQuoted, 64));

		AssertPinnedValues(
			(TextLineCommentAction.Comment, 0),
			(TextLineCommentAction.Uncomment, 1),
			(TextLineCommentAction.Toggle, 2));
	}

	[TestMethod]
	public void AutoClosingVocabularyValues_AreStable()
	{
		AssertPinnedValues(
			(TextAutoClosingActionKind.None, 0),
			(TextAutoClosingActionKind.InsertClosingText, 1),
			(TextAutoClosingActionKind.SkipExistingClosingText, 2));

		AssertPinnedValues(
			(TextAutoClosingPairKind.Bracket, 0),
			(TextAutoClosingPairKind.Quote, 1));

		AssertPinnedValues(
			(TextAutoClosingProvenance.Auto, 0),
			(TextAutoClosingProvenance.Always, 1),
			(TextAutoClosingProvenance.Never, 2));

		AssertPinnedValues(
			(IdentifierSpanMode.Containing, 0),
			(IdentifierSpanMode.EndingAtOffset, 1));
	}

	[TestMethod]
	public void RequestAndDiagnosticVocabularyValues_AreStable()
	{
		AssertPinnedValues(
			(RequestOutcome.Unknown, 0),
			(RequestOutcome.Completed, 1),
			(RequestOutcome.Canceled, 2),
			(RequestOutcome.Superseded, 3),
			(RequestOutcome.RejectedByCurrentState, 4));

		AssertPinnedValues(
			(TextDiagnosticSeverity.None, 0),
			(TextDiagnosticSeverity.Error, 1),
			(TextDiagnosticSeverity.Warning, 2),
			(TextDiagnosticSeverity.Information, 3),
			(TextDiagnosticSeverity.Hint, 4));
	}

	/// <summary>
	/// Pins every member of one enum exactly once: an added member, a renumbered member, a
	/// duplicated pin, or (for non-flags enums) a duplicated numeric value all fail the test.
	/// </summary>
	private static void AssertPinnedValues<TEnum>(params (TEnum Member, int Value)[] pinned)
		where TEnum : struct, Enum
	{
		TEnum[] members = Enum.GetValues<TEnum>();

		Assert.AreEqual(
			members.Length,
			pinned.Length,
			$"Every {typeof(TEnum).Name} member must be pinned exactly once.");

		var pinnedNames = new HashSet<string>(StringComparer.Ordinal);
		var pinnedValues = new HashSet<int>();

		foreach ((TEnum member, int value) in pinned)
		{
			string name = member.ToString();

			Assert.IsTrue(pinnedNames.Add(name), $"The {typeof(TEnum).Name} member {name} is pinned more than once.");
			Assert.AreEqual(value, Value(member), $"The value of {typeof(TEnum).Name}.{name} changed.");

			if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false))
				Assert.IsTrue(pinnedValues.Add(value), $"The value {value} is used by more than one {typeof(TEnum).Name} member.");
		}

		foreach (TEnum member in members)
			Assert.IsTrue(pinnedNames.Contains(member.ToString()), $"The {typeof(TEnum).Name} member {member} is not pinned.");
	}
}
