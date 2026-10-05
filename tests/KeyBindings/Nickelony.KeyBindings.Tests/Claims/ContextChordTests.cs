namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class ContextChordTests
{
	private static readonly KeyChord s_chord = new(new KeyStroke(KeyCode.E, KeyModifiers.None));

	[TestMethod]
	public void IsCoActiveWith_TheSameToken_ReturnsTrue() =>
		Assert.IsTrue(new ContextChord("geometry", s_chord).IsCoActiveWith(new ContextChord("geometry", s_chord)));

	[TestMethod]
	public void IsCoActiveWith_TwoDifferentTokens_ReturnsFalse() =>
		Assert.IsFalse(new ContextChord("geometry", s_chord).IsCoActiveWith(new ContextChord("texture", s_chord)));

	[TestMethod]
	public void IsCoActiveWith_EitherSideAlwaysActive_ReturnsTrue()
	{
		Assert.IsTrue(new ContextChord(ContextChord.Always, s_chord).IsCoActiveWith(new ContextChord("texture", s_chord)));
		Assert.IsTrue(new ContextChord("texture", s_chord).IsCoActiveWith(new ContextChord(ContextChord.Always, s_chord)));
	}

	[TestMethod]
	public void IsCoActiveWith_TokensThatDifferOnlyInCase_AreDifferentContexts() =>
		Assert.IsFalse(new ContextChord("Geometry", s_chord).IsCoActiveWith(new ContextChord("geometry", s_chord)));
}
