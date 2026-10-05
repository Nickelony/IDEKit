using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class KeyBindingOverrideValidationTests
{
	[TestMethod]
	public void ThrowIfMalformed_WellFormedOverrides_Passes()
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(KeyBindingOverrideCodec.CreateEntry("Save", [new KeyChord(new KeyStroke(KeyCode.S, KeyModifiers.Control))]));
		overrides.Entries.Add(new KeyBindingOverrideEntry { SerializedId = "Unbound" });

		KeyBindingOverrideValidation.ThrowIfMalformed(new KeyBindingOverrides(), "overrides");
		KeyBindingOverrideValidation.ThrowIfMalformed(overrides, "overrides");
	}

	[TestMethod]
	public void ThrowIfMalformed_UnsetMember_ThrowsNamingTheParameter()
	{
		foreach (KeyBindingOverrides malformed in CreateMalformedOverrides())
		{
			ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
				KeyBindingOverrideValidation.ThrowIfMalformed(malformed, "overridesStore")
			);

			Assert.AreEqual("overridesStore", exception.ParamName);
		}
	}

	private static IEnumerable<KeyBindingOverrides> CreateMalformedOverrides()
	{
		yield return new KeyBindingOverrides { Entries = null! };
		yield return WithEntry(null!);
		yield return WithEntry(new KeyBindingOverrideEntry { SerializedId = null!, Bindings = [] });
		yield return WithEntry(new KeyBindingOverrideEntry { SerializedId = "Save", Bindings = null! });
		yield return WithEntry(new KeyBindingOverrideEntry { SerializedId = "Save", Bindings = [null!] });
		yield return WithEntry(new KeyBindingOverrideEntry { SerializedId = "Save", Bindings = [new KeyBindingOverrideChord { Strokes = null! }] });
		yield return WithEntry(new KeyBindingOverrideEntry { SerializedId = "Save", Bindings = [new KeyBindingOverrideChord { Strokes = [null!] }] });
	}

	private static KeyBindingOverrides WithEntry(KeyBindingOverrideEntry entry)
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(entry);

		return overrides;
	}
}
