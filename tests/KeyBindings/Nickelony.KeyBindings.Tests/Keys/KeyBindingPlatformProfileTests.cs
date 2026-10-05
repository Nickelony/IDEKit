using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Pins <see cref="KeyBindingPlatformProfile"/>: the neutral default uses Control, the platform presets name their primary modifier,
/// the primary modifier is a single defined flag, <see cref="KeyBindingPlatformProfile.PrimaryStroke"/> resolves a primary-relative stroke
/// that a host can declare as a catalog default, and every member, including the modifier order and the key text overrides, participates in equality.
/// </summary>
[TestClass]
public sealed class KeyBindingPlatformProfileTests
{
	[TestMethod]
	public void Default_UsesControlAsThePrimaryModifier() => Assert.AreEqual(KeyModifiers.Control, KeyBindingPlatformProfile.Default.PrimaryModifier);

	[TestMethod]
	public void Presets_NameTheirPrimaryModifier()
	{
		Assert.AreEqual(KeyModifiers.Control, KeyBindingPlatformProfile.Windows.PrimaryModifier);
		Assert.AreEqual(KeyModifiers.Control, KeyBindingPlatformProfile.Linux.PrimaryModifier);
		Assert.AreEqual(KeyModifiers.Meta, KeyBindingPlatformProfile.MacOS.PrimaryModifier);
	}

	[TestMethod]
	public void ForCurrentPlatform_IsThePresetOfTheRunningOperatingSystem()
	{
		KeyBindingPlatformProfile expected =
			OperatingSystem.IsMacOS() ? KeyBindingPlatformProfile.MacOS
			: OperatingSystem.IsWindows() ? KeyBindingPlatformProfile.Windows
			: KeyBindingPlatformProfile.Linux;

		Assert.AreSame(expected, KeyBindingPlatformProfile.ForCurrentPlatform);
	}

	[TestMethod]
	public void MacOS_PrimaryStroke_ResolvesToMetaAndDisplaysAsCommand()
	{
		KeyStroke stroke = KeyBindingPlatformProfile.MacOS.PrimaryStroke(KeyCode.S);

		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Meta), stroke);
		Assert.AreEqual("⌘S", new DesktopKeyDisplayTextFormatter(KeyBindingPlatformProfile.MacOS).GetDisplayText(stroke));
	}

	[TestMethod]
	public void ToString_NamesThePrimaryModifier() => Assert.Contains("PrimaryModifier = Meta", KeyBindingPlatformProfile.MacOS.ToString());

	[TestMethod]
	public void Equals_ChangedPrimaryModifier_IsNotEqual() =>
		Assert.AreNotEqual(KeyBindingPlatformProfile.Windows, KeyBindingPlatformProfile.Windows with { PrimaryModifier = KeyModifiers.Meta });

	[TestMethod]
	[DataRow(KeyModifiers.None)]
	[DataRow(KeyModifiers.Control | KeyModifiers.Shift)]
	[DataRow(KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)]
	[DataRow((KeyModifiers)16)]
	public void PrimaryModifier_NotExactlyOneDefinedFlag_Throws(KeyModifiers primaryModifier) =>
		Assert.ThrowsExactly<ArgumentException>(() => new KeyBindingPlatformProfile { PrimaryModifier = primaryModifier });

	[TestMethod]
	[DataRow(KeyModifiers.Control)]
	[DataRow(KeyModifiers.Alt)]
	[DataRow(KeyModifiers.Shift)]
	[DataRow(KeyModifiers.Meta)]
	public void PrimaryModifier_EveryDefinedSingleFlagIsAccepted(KeyModifiers primaryModifier) =>
		Assert.AreEqual(primaryModifier, new KeyBindingPlatformProfile { PrimaryModifier = primaryModifier }.PrimaryModifier);

	[TestMethod]
	public void PrimaryStroke_AppliesThePrimaryModifier() =>
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S));

	[TestMethod]
	public void PrimaryStroke_AdditionalModifiersAreCombinedWithThePrimaryModifier()
	{
		Assert.AreEqual(
			new KeyStroke(KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift),
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S, KeyModifiers.Shift)
		);
	}

	[TestMethod]
	public void PrimaryStroke_ProfileWithMetaPrimaryModifier_ResolvesToMeta()
	{
		var profile = new KeyBindingPlatformProfile { PrimaryModifier = KeyModifiers.Meta };
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Meta), profile.PrimaryStroke(KeyCode.S));
	}

	[TestMethod]
	public void PrimaryStroke_ConvertsImplicitlyToAOneStrokeChord()
	{
		KeyChord chord = KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S);

		Assert.AreEqual(1, chord.StrokeCount);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), chord.Strokes[0]);
	}

	[TestMethod]
	public void PrimaryStroke_ChordsComposeFromPrimaryStrokes()
	{
		KeyChord chord = new(KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.K), KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S));

		Assert.AreEqual(2, chord.StrokeCount);
		Assert.AreEqual(new KeyStroke(KeyCode.K, KeyModifiers.Control), chord.Strokes[0]);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), chord.Strokes[1]);
	}

	[TestMethod]
	public void PrimaryStroke_DeclaresADescriptorDefault()
	{
		var descriptor = new CommandDescriptor<TestCommand>(
			TestCommand.Save,
			nameof(TestCommand.Save),
			CommandRemappingPolicy.Remappable,
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S)
		);

		Assert.AreEqual(new KeyChord(new KeyStroke(KeyCode.S, KeyModifiers.Control)), descriptor.DefaultBindings[0]);
	}

	[TestMethod]
	public void PrimaryStroke_UndefinedKey_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => KeyBindingPlatformProfile.Default.PrimaryStroke(default));

	[TestMethod]
	public void PrimaryStroke_AdditionalModifiersWithUndefinedBits_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			KeyBindingPlatformProfile.Default.PrimaryStroke(KeyCode.S, (KeyModifiers)16)
		);

		// The diagnostic names the caller's parameter, not the internal KeyStroke constructor's.
		Assert.AreEqual("additionalModifiers", exception.ParamName);
	}

	[TestMethod]
	public void Equality_Profiles_CompareByPrimaryModifier()
	{
		Assert.AreEqual(
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifiers.Meta },
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifiers.Meta }
		);

		Assert.AreNotEqual(
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifiers.Meta },
			new KeyBindingPlatformProfile { PrimaryModifier = KeyModifiers.Control }
		);
	}

	/// <summary>
	/// Pins the equal-value shapes in one place: the same instance, a <c>with</c> copy that changed nothing, and two separately built values
	/// with the same configuration all compare equal.
	/// </summary>
	[TestMethod]
	public void Equals_EqualValues_AreEqual()
	{
		Assert.AreEqual(KeyBindingPlatformProfile.Windows, KeyBindingPlatformProfile.Windows);
		Assert.AreEqual(KeyBindingPlatformProfile.Windows, KeyBindingPlatformProfile.Windows with { });
		Assert.AreEqual(new KeyBindingPlatformProfile(), new KeyBindingPlatformProfile());
	}

	[TestMethod]
	public void BareInstance_CarriesTheCanonicalNeutralLabels()
	{
		var neutral = new KeyBindingPlatformProfile();

		Assert.AreEqual("Control", neutral.ControlLabel);
		Assert.AreEqual("Shift", neutral.ShiftLabel);
		Assert.AreEqual("Alt", neutral.AltLabel);
		Assert.AreEqual("Meta", neutral.MetaLabel);
		Assert.AreEqual("NumPad ", neutral.NumPadPrefix);
		Assert.IsEmpty(neutral.KeyTextOverrides);
		Assert.AreNotEqual(KeyBindingPlatformProfile.Windows, neutral);
	}

	[TestMethod]
	public void Equals_ChangedLabel_IsNotEqual() =>
		Assert.AreNotEqual(KeyBindingPlatformProfile.Windows, KeyBindingPlatformProfile.Windows with { MetaLabel = "Cmd" });

	[TestMethod]
	public void Equals_ReorderedModifiers_IsNotEqual()
	{
		Assert.AreNotEqual(
			KeyBindingPlatformProfile.Windows,

			KeyBindingPlatformProfile.Windows with
			{
				ModifierOrder = [KeyModifiers.Shift, KeyModifiers.Control, KeyModifiers.Alt, KeyModifiers.Meta],
			}
		);
	}

	[TestMethod]
	public void ModifierOrder_Empty_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => KeyBindingPlatformProfile.Default with { ModifierOrder = [] });

	[TestMethod]
	public void ModifierOrder_Invalid_NamesTheAssignedValueAsTheParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => KeyBindingPlatformProfile.Default with { ModifierOrder = [] });

		Assert.AreEqual("value", exception.ParamName);
	}

	[TestMethod]
	public void ModifierOrder_RepeatedModifier_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			KeyBindingPlatformProfile.Default with
			{
				ModifierOrder = [KeyModifiers.Control, KeyModifiers.Shift, KeyModifiers.Control, KeyModifiers.Alt, KeyModifiers.Meta],
			}
		);
	}

	[TestMethod]
	[DataRow(KeyModifiers.None)]
	[DataRow(KeyModifiers.Control | KeyModifiers.Shift)]
	[DataRow((KeyModifiers)16)]
	public void ModifierOrder_EntryThatIsNotOneDefinedModifier_Throws(KeyModifiers entry)
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			KeyBindingPlatformProfile.Default with
			{
				ModifierOrder = [KeyModifiers.Control, KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Meta, entry],
			}
		);
	}

	[TestMethod]
	public void ModifierOrder_MissingModifier_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			KeyBindingPlatformProfile.Default with
			{
				ModifierOrder = [KeyModifiers.Control, KeyModifiers.Shift, KeyModifiers.Alt],
			}
		);
	}

	[TestMethod]
	public void ModifierOrder_IsCopiedOnAssignment()
	{
		KeyModifiers[] order = [KeyModifiers.Meta, KeyModifiers.Alt, KeyModifiers.Shift, KeyModifiers.Control];
		KeyBindingPlatformProfile profile = KeyBindingPlatformProfile.Default with { ModifierOrder = order };

		order[0] = KeyModifiers.Control;

		Assert.AreEqual(KeyModifiers.Meta, profile.ModifierOrder[0]);
	}

	[TestMethod]
	public void Equals_SameOverridesInAnotherOrder_AreEqualAndHashAlike()
	{
		var first = new KeyBindingPlatformProfile
		{
			KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.S] = "s", [KeyCode.K] = "k" },
		};

		var second = new KeyBindingPlatformProfile
		{
			KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.K] = "k", [KeyCode.S] = "s" },
		};

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_ChangedOverrideValue_IsNotEqual()
	{
		var first = new KeyBindingPlatformProfile { KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.S] = "s" } };
		var second = new KeyBindingPlatformProfile { KeyTextOverrides = new Dictionary<KeyCode, string> { [KeyCode.S] = "S" } };

		Assert.AreNotEqual(first, second);
	}

	/// <summary>
	/// Pins <c>Equals(object)</c> and the <see langword="null"/> case: a record compares only with another profile, so a <see langword="null"/>
	/// or a value of another type is never equal.
	/// </summary>
	[TestMethod]
	public void Equals_NullOrOtherType_IsFalse()
	{
		KeyBindingPlatformProfile windows = KeyBindingPlatformProfile.Windows;

		Assert.IsFalse(((object)windows).Equals(null));
		Assert.IsFalse(((object)windows).Equals("not a profile"));
		Assert.IsTrue(((object)windows).Equals(KeyBindingPlatformProfile.Windows with { }));
	}

	/// <summary>Pins that every remaining member participates in equality, not only the labels and the modifier order.</summary>
	[TestMethod]
	public void Equals_MemberDifferences_AreNotEqual()
	{
		KeyBindingPlatformProfile windows = KeyBindingPlatformProfile.Windows;

		Assert.AreNotEqual(windows, windows with { NumPadPrefix = "Num" });
		Assert.AreNotEqual(windows, windows with { StrokeSeparator = "|" });
		Assert.AreNotEqual(windows, windows with { ModifierSeparator = "-" });
		Assert.AreNotEqual(windows, windows with { ShiftLabel = "Sh" });
		Assert.AreNotEqual(windows, windows with { AltLabel = "Option" });

		// A modifier order of a different length is a different sequence, even though the entries it
		// names are the same flags.
		Assert.AreNotEqual(windows, windows with { ModifierOrder = [KeyModifiers.Control, KeyModifiers.Alt, KeyModifiers.Shift, KeyModifiers.Meta] });
	}

	/// <summary>
	/// Pins the dictionary comparison: an override set with the same count but a different key is not equal, so a count-only check would silently
	/// accept it.
	/// </summary>
	[TestMethod]
	public void Equals_EqualCountWithADifferentKey_IsNotEqual()
	{
		var mutated = new Dictionary<KeyCode, string>(KeyBindingPlatformProfile.Windows.KeyTextOverrides);
		mutated.Remove(KeyCode.Escape);
		mutated[KeyCode.Enter] = "Return";

		Assert.HasCount(KeyBindingPlatformProfile.Windows.KeyTextOverrides.Count, mutated);
		Assert.AreNotEqual(KeyBindingPlatformProfile.Windows, KeyBindingPlatformProfile.Windows with { KeyTextOverrides = mutated });
	}

	/// <summary>
	/// Pins the short <see cref="object.ToString"/> form: it names the labels and prints only the override count, so a host that logs its options
	/// does not flood the log with every key entry.
	/// </summary>
	[TestMethod]
	public void ToString_SummarizesWithoutEveryOverride()
	{
		string text = KeyBindingPlatformProfile.Windows.ToString();

		Assert.Contains(nameof(KeyBindingPlatformProfile.ControlLabel), text);
		Assert.Contains("KeyTextOverrides = ", text);
		Assert.DoesNotContain(nameof(KeyCode.Backslash), text, "ToString must not print every override entry.");
	}
}
