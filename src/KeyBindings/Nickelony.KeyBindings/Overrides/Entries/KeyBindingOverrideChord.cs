using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Nickelony.KeyBindings.OverrideEntries;

/// <summary>One persisted binding: the ordered <see cref="KeyBindingOverrideStroke"/> sequence that forms a <see cref="KeyChord"/>.</summary>
/// <remarks>
/// <see cref="Strokes"/> is a mutable <see cref="List{T}"/> property with a public setter so the binding can round-trip through the serializers;
/// the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/KeyBindingsGuide.md#persistence">key bindings guide</see> explains
/// the exception to the <c>IReadOnlyList&lt;T&gt;</c> payload rule.
/// </remarks>
public sealed class KeyBindingOverrideChord
{
	public KeyBindingOverrideChord() => Strokes = [];

	/// <summary>The strokes of this binding, in press order.</summary>
	/// <remarks>
	/// An empty stroke list has no meaning and is skipped when overrides are loaded.
	/// </remarks>
	[XmlElement("Stroke")]
	[JsonPropertyName("strokes")]
	public List<KeyBindingOverrideStroke> Strokes { get; set; }

	/// <summary>Creates a deep copy of this binding, including every stroke.</summary>
	/// <returns>The copied binding.</returns>
	public KeyBindingOverrideChord Clone()
	{
		var clone = new KeyBindingOverrideChord();

		foreach (KeyBindingOverrideStroke stroke in Strokes)
			clone.Strokes.Add(stroke.Clone());

		return clone;
	}
}
