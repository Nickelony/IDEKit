using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Tests the persisted-override codec directly, since it is the single home of the strict stored grammar
/// (canonical names only, no numeric or alias form) that the public serialization surface depends on.
/// </summary>
[TestClass]
public sealed class KeyBindingOverrideCodecTests
{
	private static readonly KeyBindingOverrideCodec s_codec = new(NullLogger.Instance);

	/// <summary>
	/// Pins that every canonical member name survives a write/read round-trip, so a persisted override never resolves to a different key
	/// after a rename.
	/// </summary>
	[TestMethod]
	public void EveryKeyCode_RoundTripsThroughTheCodec()
	{
		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
		{
			var chord = new KeyChord(new KeyStroke(keyCode, KeyModifiers.Control));

			KeyBindingOverrideChord binding = KeyBindingOverrideCodec.CreateBinding(chord);
			KeyChord? parsed = s_codec.ParseBinding(binding, "Save", logDiagnostics: false);

			Assert.IsNotNull(parsed, $"KeyCode.{keyCode} did not round-trip.");
			Assert.AreEqual(chord, parsed.Value, $"KeyCode.{keyCode} did not round-trip to the same chord.");
		}
	}

	[TestMethod]
	public void MultiStrokeChord_RoundTripsInOrder()
	{
		var chord = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.None));

		KeyChord? parsed = s_codec.ParseBinding(KeyBindingOverrideCodec.CreateBinding(chord), "Save", logDiagnostics: false);

		Assert.IsNotNull(parsed);
		Assert.AreEqual(chord, parsed.Value);
	}

	/// <summary>
	/// Pins that the strict grammar accepts only exact canonical member names: a numeric string and the framework alias names are rejected
	/// instead of resolving to the same key.
	/// </summary>
	[TestMethod]
	[DataRow("62")]
	[DataRow("Oem1")]
	[DataRow("Next")]
	[DataRow("Snapshot")]
	[DataRow(" S")]
	[DataRow("S ")]
	public void NonCanonicalKeyName_IsRejected(string keyName)
	{
		var binding = new KeyBindingOverrideChord();
		binding.Strokes.Add(new KeyBindingOverrideStroke { KeyName = keyName, Modifiers = 0 });

		Assert.IsNull(s_codec.ParseBinding(binding, "Save", logDiagnostics: false));
	}

	[TestMethod]
	public void EmptyStrokeList_DropsTheWholeBinding() =>
		Assert.IsNull(s_codec.ParseBinding(new KeyBindingOverrideChord(), "Save", logDiagnostics: false));

	/// <summary>Pins that a binding that repeats a stroke is dropped as a whole, because a chord never presses the same stroke twice.</summary>
	[TestMethod]
	public void RepeatedStroke_DropsTheWholeBinding()
	{
		var binding = new KeyBindingOverrideChord();

		binding.Strokes.Add(new KeyBindingOverrideStroke { KeyName = "S", Modifiers = 2 });
		binding.Strokes.Add(new KeyBindingOverrideStroke { KeyName = "S", Modifiers = 2 });

		Assert.IsNull(s_codec.ParseBinding(binding, "Save", logDiagnostics: false));
	}

	/// <summary>
	/// Pins that modifier bits outside the defined flags are rejected, so an unknown bit never becomes a silently different combination.
	/// </summary>
	[TestMethod]
	public void UnknownModifierBits_AreRejected()
	{
		var binding = new KeyBindingOverrideChord();

		binding.Strokes.Add(
			new KeyBindingOverrideStroke
			{
				KeyName = "S",
				Modifiers = (int)(KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta) + 1,
			}
		);

		Assert.IsNull(s_codec.ParseBinding(binding, "Save", logDiagnostics: false));
	}

	/// <summary>Pins that an entry drops a chord it already declared, keeping only the first occurrence.</summary>
	[TestMethod]
	public void ParseEntryBindings_DropsARepeatedChord()
	{
		var chord = new KeyChord(new KeyStroke(KeyCode.S, KeyModifiers.Control));
		var entry = new KeyBindingOverrideEntry { SerializedId = "Save" };

		entry.Bindings.Add(KeyBindingOverrideCodec.CreateBinding(chord));
		entry.Bindings.Add(KeyBindingOverrideCodec.CreateBinding(chord));

		List<KeyChord> parsed = s_codec.ParseEntryBindings(entry, "Save", logDiagnostics: false);

		Assert.HasCount(1, parsed);
		Assert.AreEqual(chord, parsed[0]);
	}

	[TestMethod]
	public void ParseEntryBindings_SkipsOnlyTheInvalidBinding()
	{
		var valid = new KeyChord(new KeyStroke(KeyCode.S, KeyModifiers.Control));
		var invalid = new KeyBindingOverrideChord();

		invalid.Strokes.Add(new KeyBindingOverrideStroke { KeyName = "NotAKey", Modifiers = 0 });

		var entry = new KeyBindingOverrideEntry { SerializedId = "Save" };

		entry.Bindings.Add(KeyBindingOverrideCodec.CreateBinding(valid));
		entry.Bindings.Add(invalid);

		List<KeyChord> parsed = s_codec.ParseEntryBindings(entry, "Save", logDiagnostics: false);

		Assert.HasCount(1, parsed);
		Assert.AreEqual(valid, parsed[0]);
	}
}
