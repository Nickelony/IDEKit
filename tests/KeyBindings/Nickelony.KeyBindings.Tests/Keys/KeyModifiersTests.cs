namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class KeyModifiersTests
{
	/// <summary>
	/// Pins the persisted flag numbering: a stroke stores its modifiers as the <see cref="KeyModifiers"/> flag number, so a renumbering must fail
	/// loudly instead of silently changing what a stored document means.
	/// </summary>
	[TestMethod]
	[DataRow(KeyModifiers.None, 0)]
	[DataRow(KeyModifiers.Alt, 1)]
	[DataRow(KeyModifiers.Control, 2)]
	[DataRow(KeyModifiers.Shift, 4)]
	[DataRow(KeyModifiers.Meta, 8)]
	public void DefinedFlags_KeepTheirPersistedValue(KeyModifiers modifier, int expectedValue) => Assert.AreEqual(expectedValue, (int)modifier);

	/// <summary>
	/// Pins that the predicate treats exactly the four flags as defined, so a combination outside them always carries an unknown bit.
	/// </summary>
	[TestMethod]
	public void HasUndefinedFlags_IsFalseExactlyForTheDefinedFlags()
	{
		KeyModifiers[] defined = Enum.GetValues<KeyModifiers>();

		for (int bit = 0; bit < 31; bit++)
		{
			var modifiers = (KeyModifiers)(1 << bit);
			Assert.AreEqual(!defined.Contains(modifiers), modifiers.HasUndefinedFlags(), $"Bit {bit} is classified wrongly.");
		}

		Assert.IsFalse(KeyModifiers.None.HasUndefinedFlags());
	}

	/// <summary>Pins that every defined flag is a distinct single bit, so any value that combines them stays unambiguous.</summary>
	[TestMethod]
	public void DefinedFlags_AreDistinctSingleBits()
	{
		KeyModifiers[] flags = [KeyModifiers.Alt, KeyModifiers.Control, KeyModifiers.Shift, KeyModifiers.Meta];

		int seen = 0;

		foreach (KeyModifiers flag in flags)
		{
			int value = (int)flag;

			Assert.AreEqual(0, value & (value - 1), $"KeyModifiers.{flag} must be a single bit.");
			Assert.AreEqual(0, seen & value, $"KeyModifiers.{flag} must not repeat a bit.");

			seen |= value;
		}

		Assert.IsFalse(((KeyModifiers)seen).HasUndefinedFlags());
		Assert.IsTrue(((KeyModifiers)(seen + 1)).HasUndefinedFlags());
	}
}
