using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Nickelony.KeyBindings;

/// <summary>
/// One persisted binding: the ordered <see cref="KeyBindingOverrideStroke"/> sequence that forms a
/// <see cref="KeyChord"/>.
/// </summary>
/// <remarks>
/// A binding with a single stroke is a plain key combo. A binding with several strokes is a chord; the
/// strokes are pressed in order and each carries its own modifiers. <see cref="Strokes"/> is a mutable
/// <see cref="List{T}"/> property with a public setter so the binding can round-trip through the
/// serializers; see <see cref="KeyBindingOverrides"/> for the exception to the
/// <c>IReadOnlyList&lt;T&gt;</c> payload rule.
/// </remarks>
public sealed class KeyBindingOverrideBinding
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingOverrideBinding"/> class.
	/// </summary>
	public KeyBindingOverrideBinding()
	{
		Strokes = [];
	}

	/// <summary>
	/// Gets or sets the strokes of this binding, in press order.
	/// </summary>
	/// <remarks>
	/// An empty stroke list has no meaning and is skipped when overrides are loaded; a binding whose
	/// strokes cannot all be parsed is skipped as a whole, because a chord with a missing stroke is not
	/// the declared chord.
	/// </remarks>
	[XmlElement("Stroke")]
	[JsonPropertyName("strokes")]
	public List<KeyBindingOverrideStroke> Strokes { get; set; }

	/// <summary>
	/// Creates a deep copy of this binding, including every stroke.
	/// </summary>
	/// <remarks>
	/// A list a caller left unset (<see langword="null"/>) is treated as empty and a
	/// <see langword="null"/> element is skipped, so the copy is always usable.
	/// </remarks>
	/// <returns>The copied binding.</returns>
	public KeyBindingOverrideBinding Clone()
	{
		var clone = new KeyBindingOverrideBinding();

		if (Strokes is null)
			return clone;

		foreach (KeyBindingOverrideStroke stroke in Strokes)
		{
			if (stroke is not null)
				clone.Strokes.Add(stroke.Clone());
		}

		return clone;
	}
}
