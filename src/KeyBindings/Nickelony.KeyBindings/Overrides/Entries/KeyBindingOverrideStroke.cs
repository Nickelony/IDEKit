using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Nickelony.KeyBindings.OverrideEntries;

/// <summary>Key and modifier values for one persisted stroke.</summary>
/// <remarks>
/// A stroke is one press inside a <see cref="KeyBindingOverrideChord"/>: a one-stroke binding is a persisted <see cref="KeyStroke"/>
/// and a multi-stroke binding a persisted <see cref="KeyChord"/>. The members use the persisted shapes, <see cref="KeyName"/> the canonical
/// <see cref="KeyCode"/> member name and <see cref="Modifiers"/> the raw <see cref="KeyModifiers"/> flag number.
/// </remarks>
public sealed class KeyBindingOverrideStroke
{
	public KeyBindingOverrideStroke() => KeyName = string.Empty;

	/// <summary>The <see cref="KeyCode"/> member name, such as <c>S</c>, <c>F9</c>, or <c>Slash</c>.</summary>
	/// <remarks>
	/// The name is the canonical serialized name of a <see cref="KeyCode"/> member. Loading accepts only exact member names;
	/// numeric strings and alias names that other frameworks use for the same key, such as <c>Oem1</c>, <c>Next</c>, or <c>Snapshot</c>, are ignored.
	/// </remarks>
	[XmlAttribute("Key")]
	[JsonPropertyName("key")]
	public string KeyName { get; set; }

	/// <summary>The numeric <see cref="KeyModifiers"/> flag combination pressed with the key.</summary>
	/// <remarks>
	/// The property stays an <see cref="int"/> because the schema stores the flag combination as a number in both formats,
	/// so the <see cref="KeyModifiers"/> numbering is a host-facing contract rather than an implementation detail. A stroke whose value sets bits
	/// outside the defined flags is skipped, and its binding with it, when overrides are loaded.
	/// </remarks>
	[XmlAttribute("Modifiers")]
	[JsonPropertyName("modifiers")]
	public int Modifiers { get; set; }

	/// <summary>Creates a copy of this stroke.</summary>
	/// <returns>The copied stroke.</returns>
	public KeyBindingOverrideStroke Clone() => new() { KeyName = KeyName, Modifiers = Modifiers };
}
