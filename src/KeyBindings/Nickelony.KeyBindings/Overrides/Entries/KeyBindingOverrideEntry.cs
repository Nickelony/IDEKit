using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Nickelony.KeyBindings.OverrideEntries;

/// <summary>A single command override entry in the persisted key binding settings.</summary>
public sealed class KeyBindingOverrideEntry
{
	public KeyBindingOverrideEntry()
	{
		SerializedId = string.Empty;
		Bindings = [];
	}

	/// <summary>The stable serialized identifier of the catalog descriptor this entry overrides.</summary>
	/// <remarks>
	/// The value is matched ordinally (case-sensitive) against <see cref="CommandDescriptor{TCommandId}.SerializedId"/>.
	/// </remarks>
	[XmlAttribute("Id")]
	[JsonPropertyName("id")]
	public string SerializedId { get; set; }

	/// <summary>The persisted bindings the command uses, in stored order.</summary>
	/// <remarks>
	/// An empty list explicitly unbinds the command; repeated chords in one entry are ignored after the first.
	/// </remarks>
	[XmlElement("Binding")]
	[JsonPropertyName("bindings")]
	public List<KeyBindingOverrideChord> Bindings { get; set; }

	/// <summary>Creates a deep copy of this entry, including every binding.</summary>
	/// <returns>The copied entry.</returns>
	public KeyBindingOverrideEntry Clone()
	{
		var clone = new KeyBindingOverrideEntry { SerializedId = SerializedId };

		foreach (KeyBindingOverrideChord binding in Bindings)
			clone.Bindings.Add(binding.Clone());

		return clone;
	}
}
