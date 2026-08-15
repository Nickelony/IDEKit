namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Pins the value semantics of <see cref="DesktopKeyDisplayConventions"/>: every member, including the
/// <see cref="DesktopKeyDisplayConventions.ModifierOrder"/> sequence and the
/// <see cref="DesktopKeyDisplayConventions.KeyTextOverrides"/> entries, participates in equality, so
/// two conventions that were configured the same way compare equal.
/// </summary>
[TestClass]
public class DesktopKeyDisplayConventionsTests
{
	/// <summary>
	/// Pins the equal-value shapes in one place: the same instance, a <c>with</c> copy that changed
	/// nothing, and two separately built values with the same configuration all compare equal.
	/// </summary>
	[TestMethod]
	public void Equals_EqualValues_AreEqual()
	{
		Assert.AreEqual(DesktopKeyDisplayConventions.Windows, DesktopKeyDisplayConventions.Windows);
		Assert.AreEqual(DesktopKeyDisplayConventions.Windows, DesktopKeyDisplayConventions.Windows with { });
		Assert.AreEqual(new DesktopKeyDisplayConventions(), new DesktopKeyDisplayConventions());
	}

	[TestMethod]
	public void BareInstance_CarriesTheCanonicalNeutralLabels()
	{
		var neutral = new DesktopKeyDisplayConventions();

		Assert.AreEqual("Control", neutral.ControlLabel);
		Assert.AreEqual("Shift", neutral.ShiftLabel);
		Assert.AreEqual("Alt", neutral.AltLabel);
		Assert.AreEqual("Meta", neutral.MetaLabel);
		Assert.AreEqual("Numpad ", neutral.NumpadPrefix);
		Assert.AreEqual(0, neutral.KeyTextOverrides.Count);
		Assert.AreNotEqual(DesktopKeyDisplayConventions.Windows, neutral);
	}

	[TestMethod]
	public void Equals_ChangedLabel_IsNotEqual()
		=> Assert.AreNotEqual(
			DesktopKeyDisplayConventions.Windows,
			DesktopKeyDisplayConventions.Windows with { MetaLabel = "Cmd" });

	[TestMethod]
	public void Equals_ReorderedModifiers_IsNotEqual()
		=> Assert.AreNotEqual(
			DesktopKeyDisplayConventions.Windows,
			DesktopKeyDisplayConventions.Windows with
			{
				ModifierOrder = [KeyModifierSet.Shift, KeyModifierSet.Control, KeyModifierSet.Alt, KeyModifierSet.Meta]
			});

	[TestMethod]
	public void Equals_SameOverridesInAnotherOrder_AreEqualAndHashAlike()
	{
		var first = new DesktopKeyDisplayConventions
		{
			KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.S] = "s", [KeyCode.K] = "k" }
		};
		var second = new DesktopKeyDisplayConventions
		{
			KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.K] = "k", [KeyCode.S] = "s" }
		};

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_ChangedOverrideValue_IsNotEqual()
	{
		var first = new DesktopKeyDisplayConventions
		{
			KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.S] = "s" }
		};
		var second = new DesktopKeyDisplayConventions
		{
			KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.S] = "S" }
		};

		Assert.AreNotEqual(first, second);
	}

	/// <summary>
	/// Pins <c>Equals(object)</c> and the null case: a record compares only with another convention, so
	/// a null or a value of another type is never equal.
	/// </summary>
	[TestMethod]
	public void Equals_NullOrOtherType_IsFalse()
	{
		DesktopKeyDisplayConventions windows = DesktopKeyDisplayConventions.Windows;

		Assert.IsFalse(((object)windows).Equals(null));
		Assert.IsFalse(((object)windows).Equals("not a convention"));
		Assert.IsTrue(((object)windows).Equals(DesktopKeyDisplayConventions.Windows with { }));
	}

	/// <summary>
	/// Pins that every remaining member participates in equality, not only the labels and the modifier
	/// order.
	/// </summary>
	[TestMethod]
	public void Equals_MemberDifferences_AreNotEqual()
	{
		DesktopKeyDisplayConventions windows = DesktopKeyDisplayConventions.Windows;

		Assert.AreNotEqual(windows, windows with { NumpadPrefix = "Num" });
		Assert.AreNotEqual(windows, windows with { StrokeSeparator = "|" });
		Assert.AreNotEqual(windows, windows with { ModifierSeparator = "-" });
		Assert.AreNotEqual(windows, windows with { ShiftLabel = "Sh" });
		Assert.AreNotEqual(windows, windows with { AltLabel = "Option" });

		// A modifier order of a different length is a different sequence, even though the entries it
		// names are the same flags.
		Assert.AreNotEqual(windows, windows with { ModifierOrder = [KeyModifierSet.Control, KeyModifierSet.Shift, KeyModifierSet.Alt] });
	}

	/// <summary>
	/// Pins the dictionary comparison: an override set with the same count but a different key is not
	/// equal, so a count-only check would silently accept it.
	/// </summary>
	[TestMethod]
	public void Equals_EqualCountWithADifferentKey_IsNotEqual()
	{
		var mutated = new Dictionary<KeyCode, string>(DesktopKeyDisplayConventions.Windows.KeyTextOverrides);
		mutated.Remove(KeyCode.Escape);
		mutated[KeyCode.Enter] = "Return";

		Assert.AreEqual(DesktopKeyDisplayConventions.Windows.KeyTextOverrides.Count, mutated.Count);
		Assert.AreNotEqual(DesktopKeyDisplayConventions.Windows, DesktopKeyDisplayConventions.Windows with { KeyTextOverrides = mutated });
	}
}
