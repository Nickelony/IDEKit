using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Pins the <see cref="KeyBindingPlatformProfile"/> seam: the neutral default uses Control, the primary
/// modifier is a single defined flag, and <see cref="KeyBindingPlatformProfile.PrimaryStroke"/> resolves a
/// primary-relative stroke that a host can declare as a catalog default.
/// </summary>
[TestClass]
public sealed class KeyBindingPlatformProfileTests
{
	[TestMethod]
	public void Default_UsesControlAsThePrimaryModifier()
		=> Assert.AreEqual(KeyModifierSet.Control, KeyBindingPlatformProfile.Default.PrimaryModifier);

	[TestMethod]
	[DataRow(KeyModifierSet.None)]
	[DataRow(KeyModifierSet.Control | KeyModifierSet.Shift)]
	[DataRow(KeyModifierSet.Alt | KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Meta)]
	[DataRow((KeyModifierSet)16)]
	public void PrimaryModifier_NotExactlyOneDefinedFlag_Throws(KeyModifierSet primaryModifier)
		=> Assert.ThrowsExactly<ArgumentException>(() => new KeyBindingPlatformProfile { PrimaryModifier = primaryModifier });

	[TestMethod]
	[DataRow(KeyModifierSet.Control)]
	[DataRow(KeyModifierSet.Alt)]
	[DataRow(KeyModifierSet.Shift)]
	[DataRow(KeyModifierSet.Meta)]
	public void PrimaryModifier_EveryDefinedSingleFlagIsAccepted(KeyModifierSet primaryModifier)
		=> Assert.AreEqual(primaryModifier, new KeyBindingPlatformProfile { PrimaryModifier = primaryModifier }.PrimaryModifier);

	[TestMethod]
	public void PrimaryStroke_AppliesThePrimaryModifier()
		=> Assert.AreEqual(
			new KeyCombo(KeyCode.S, KeyModifierSet.Control),
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S));

	[TestMethod]
	public void PrimaryStroke_AdditionalModifiersAreCombinedWithThePrimaryModifier()
		=> Assert.AreEqual(
			new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift),
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S, KeyModifierSet.Shift));

	[TestMethod]
	public void PrimaryStroke_ProfileWithMetaPrimaryModifier_ResolvesToMeta()
	{
		var profile = new KeyBindingPlatformProfile { PrimaryModifier = KeyModifierSet.Meta };

		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Meta), profile.PrimaryStroke(KeyCode.S));
	}

	[TestMethod]
	public void PrimaryStroke_ConvertsImplicitlyToAOneStrokeChord()
	{
		KeyChord chord = KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S);

		Assert.AreEqual(1, chord.StrokeCount);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), chord.Strokes[0]);
	}

	[TestMethod]
	public void PrimaryStroke_ChordsComposeFromPrimaryStrokes()
	{
		KeyChord chord = new(
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.K),
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S));

		Assert.AreEqual(2, chord.StrokeCount);
		Assert.AreEqual(new KeyCombo(KeyCode.K, KeyModifierSet.Control), chord.Strokes[0]);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), chord.Strokes[1]);
	}

	[TestMethod]
	public void PrimaryStroke_DeclaresADescriptorDefault()
	{
		var descriptor = new CommandDescriptor<TestCommand>(
			TestCommand.Save,
			nameof(TestCommand.Save),
			CommandRemappingPolicy.Remappable,
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S));

		Assert.AreEqual(new KeyChord(new KeyCombo(KeyCode.S, KeyModifierSet.Control)), descriptor.DefaultBindings[0]);
	}

	[TestMethod]
	public void PrimaryStroke_UndefinedKey_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => KeyBindingPlatformProfile.Default.PrimaryStroke(default));

	[TestMethod]
	public void PrimaryStroke_AdditionalModifiersWithUndefinedBits_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(
			() => KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S, (KeyModifierSet)16));

	[TestMethod]
	public void Equality_Profiles_CompareByPrimaryModifier()
	{
		Assert.AreEqual(
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifierSet.Meta },
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifierSet.Meta });
		Assert.AreNotEqual(
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifierSet.Meta },
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifierSet.Control });
	}
}
