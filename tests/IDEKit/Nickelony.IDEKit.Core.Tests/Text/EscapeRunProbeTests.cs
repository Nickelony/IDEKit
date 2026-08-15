namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class EscapeRunProbeTests
{
	[TestMethod]
	public void IsEscaped_CountsAnOddRunImmediatelyBeforeTheToken()
	{
		Assert.IsTrue(IsEscaped("\\", '\\'));
		Assert.IsFalse(IsEscaped("\\\\", '\\'));
		Assert.IsTrue(IsEscaped("\\\\\\", '\\'));
	}

	[TestMethod]
	public void IsEscaped_StopsAtTheFirstCharacterOutsideTheRun()
	{
		// The run ends at the token: a character that is not the escape character ends it, so text
		// before the run does not change the answer, and the start of the text ends it too.
		Assert.IsFalse(IsEscaped("a", '\\'));
		Assert.IsFalse(IsEscaped("\\a", '\\'));
		Assert.IsTrue(IsEscaped("a\\", '\\'));
		Assert.IsFalse(IsEscaped(string.Empty, '\\'));
	}

	[TestMethod]
	public void IsEscaped_UsesTheSuppliedEscapeCharacter()
	{
		Assert.IsTrue(IsEscaped("~~~", '~'));
		Assert.IsFalse(IsEscaped("\\\\\\", '~'));
	}

	private static bool IsEscaped(string precedingText, char escapeCharacter)
	{
		var probe = new EscapeRunProbe(escapeCharacter);

		for (int index = precedingText.Length - 1; index >= 0 && probe.Consume(precedingText[index]); index--)
		{
		}

		return probe.IsEscaped;
	}
}
