using static Nickelony.IDEKit.Core.Tests.EnumValueTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class EnumValueStabilityTests
{
	[TestMethod]
	public void CommentVocabularyValues_AreStable()
	{
		AssertPinnedValues((CommentKind.Line, 0), (CommentKind.Block, 1));

		AssertPinnedValues(
			(StringLiteralStyles.None, 0),
			(StringLiteralStyles.DoubleQuoted, 1),
			(StringLiteralStyles.SingleQuoted, 2),
			(StringLiteralStyles.BacktickQuoted, 4),
			(StringLiteralStyles.TripleDoubleQuoted, 8),
			(StringLiteralStyles.TripleSingleQuoted, 16),
			(StringLiteralStyles.LongBracketQuoted, 32),
			(StringLiteralStyles.VerbatimDoubleQuoted, 64)
		);

		AssertPinnedValues((LineCommentActionKind.Comment, 0), (LineCommentActionKind.Uncomment, 1), (LineCommentActionKind.Toggle, 2));
	}

	[TestMethod]
	public void AutoClosingVocabularyValues_AreStable()
	{
		AssertPinnedValues(
			(AutoClosingActionKind.None, 0),
			(AutoClosingActionKind.InsertClosingText, 1),
			(AutoClosingActionKind.SkipExistingClosingText, 2)
		);

		AssertPinnedValues((AutoClosingPairKind.Bracket, 0), (AutoClosingPairKind.Quote, 1));

		AssertPinnedValues((AutoClosingTrackingMode.TrackedOnly, 0), (AutoClosingTrackingMode.Always, 1), (AutoClosingTrackingMode.Never, 2));

		AssertPinnedValues((IdentifierSpanMode.Containing, 0), (IdentifierSpanMode.EndingAtOffset, 1), (IdentifierSpanMode.AtOffset, 2));
	}

	[TestMethod]
	public void RequestAndDiagnosticVocabularyValues_AreStable()
	{
		AssertPinnedValues(
			(RequestOutcome.Unknown, 0),
			(RequestOutcome.Completed, 1),
			(RequestOutcome.Canceled, 2),
			(RequestOutcome.Superseded, 3),
			(RequestOutcome.RejectedByCurrentState, 4)
		);

		AssertPinnedValues(
			(DiagnosticSeverity.None, 0),
			(DiagnosticSeverity.Error, 1),
			(DiagnosticSeverity.Warning, 2),
			(DiagnosticSeverity.Information, 3),
			(DiagnosticSeverity.Hint, 4)
		);
	}

	/// <summary>
	/// Pins every member of one enum exactly once: an added member, a renumbered member, a duplicated pin, or (for non-flags enums)
	/// a duplicated numeric value all fail the test.
	/// </summary>
	private static void AssertPinnedValues<TEnum>(params (TEnum Member, int Value)[] pinned)
		where TEnum : struct, Enum
	{
		TEnum[] members = Enum.GetValues<TEnum>();

		Assert.HasCount(members.Length, pinned, $"Every {typeof(TEnum).Name} member must be pinned exactly once.");

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
			Assert.Contains(member.ToString(), pinnedNames, $"The {typeof(TEnum).Name} member {member} is not pinned.");
	}
}
