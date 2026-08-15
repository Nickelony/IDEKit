using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Nickelony.KeyBindings;

/// <summary>
/// Key and modifier values for one persisted stroke.
/// </summary>
/// <remarks>
/// <para>
/// A stroke is one press inside a <see cref="KeyBindingOverrideBinding"/>; a binding with a single
/// stroke is the persisted form of a plain <see cref="KeyCombo"/>, and a binding with several strokes
/// is the persisted form of a <see cref="KeyChord"/>.
/// </para>
/// <para>
/// The two members deliberately use the persisted shapes rather than the domain types:
/// <see cref="KeyName"/> is the canonical <see cref="KeyCode"/> member name (a string, so a document
/// stays readable and a value this package does not define is not silently accepted) and
/// <see cref="Modifiers"/> is the raw <see cref="KeyModifierSet"/> flag number. Both formats therefore
/// store a stable, host-facing value instead of this package's CLR representation.
/// </para>
/// </remarks>
public sealed class KeyBindingOverrideStroke
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingOverrideStroke"/> class.
	/// </summary>
	public KeyBindingOverrideStroke()
	{
		KeyName = string.Empty;
	}

	/// <summary>
	/// Gets or sets the <see cref="KeyCode"/> member name, such as <c>S</c>, <c>F9</c>, or
	/// <c>Slash</c>.
	/// </summary>
	/// <remarks>
	/// The name is the canonical serialized name of a <see cref="KeyCode"/> member. Loading accepts
	/// only exact member names; numeric strings and alias names that other frameworks use for the
	/// same key, such as <c>Oem1</c>, <c>Next</c>, or <c>Snapshot</c>, are ignored.
	/// </remarks>
	[XmlAttribute("Key")]
	[JsonPropertyName("key")]
	public string KeyName { get; set; }

	/// <summary>
	/// Gets or sets the numeric <see cref="KeyModifierSet"/> flag combination pressed with the key.
	/// </summary>
	/// <remarks>
	/// The property stays an <see cref="int"/> because the schema stores the flag combination as a number
	/// in both formats, so the <see cref="KeyModifierSet"/> numbering is a host-facing contract rather than an
	/// implementation detail. A binding entry whose value sets bits outside the defined flags is skipped
	/// when overrides are loaded.
	/// </remarks>
	[XmlAttribute("Modifiers")]
	[JsonPropertyName("modifiers")]
	public int Modifiers { get; set; }

	/// <summary>
	/// Creates a copy of this stroke.
	/// </summary>
	/// <returns>The copied stroke.</returns>
	public KeyBindingOverrideStroke Clone()
		=> new()
		{
			KeyName = KeyName,
			Modifiers = Modifiers
		};
}
