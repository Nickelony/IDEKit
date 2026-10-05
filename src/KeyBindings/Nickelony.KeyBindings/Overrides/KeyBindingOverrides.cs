using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings;

/// <summary>Collection of key binding overrides with a schema version, serializable as XML or JSON.</summary>
/// <remarks>
/// An override entry replaces its command's catalog defaults:
/// the command claims exactly the entry's bindings, an empty list unbinds it, and a <see cref="CommandRemappingPolicy.HostReserved"/>
/// command ignores its entry. An entry whose bindings are all invalid falls back to the catalog defaults and logs a warning when the overrides load;
/// the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/KeyBindingsGuide.md#logging">key bindings guide</see>
/// lists the event ids. Only <see cref="CurrentVersion"/> is read or written; any other version is rejected. The collections stay mutable
/// <see cref="List{T}"/> properties so the serializers can populate them.
/// </remarks>
public sealed class KeyBindingOverrides
{
	/// <summary>The schema version written and read by this package.</summary>
	public const int CurrentVersion = 3;

	public KeyBindingOverrides()
	{
		Version = CurrentVersion;
		Entries = [];
	}

	/// <summary>
	/// The schema version, written as the <c>Version</c> XML attribute and the <c>version</c> JSON member; a new collection starts
	/// at <see cref="CurrentVersion"/>.
	/// </summary>
	[XmlAttribute("Version")]
	[JsonPropertyName("version")]
	public int Version { get; set; }

	[XmlElement("Command")]
	[JsonPropertyName("commands")]
	public List<KeyBindingOverrideEntry> Entries { get; set; }

	/// <summary>Creates a deep copy of this collection, including every entry and binding.</summary>
	/// <returns>The copied collection with the same <see cref="Version"/> as this instance.</returns>
	public KeyBindingOverrides Clone() => CloneWhere(static _ => true);

	/// <summary>Copies the entries that satisfy a predicate into a new collection of the same version.</summary>
	internal KeyBindingOverrides CloneWhere(Func<KeyBindingOverrideEntry, bool> keep)
	{
		var clone = new KeyBindingOverrides { Version = Version };

		foreach (KeyBindingOverrideEntry entry in Entries)
		{
			if (keep(entry))
				clone.Entries.Add(entry.Clone());
		}

		return clone;
	}

	/// <summary>Copies every entry except the one for a command into a new collection of the same version.</summary>
	internal KeyBindingOverrides CloneWithout(string serializedId) =>
		CloneWhere(entry => !string.Equals(entry.SerializedId, serializedId, StringComparison.Ordinal));

	/// <summary>Returns the entry for a command, or <see langword="null"/> when the collection has none.</summary>
	internal KeyBindingOverrideEntry? Find(string serializedId) =>
		Entries.Find(entry => string.Equals(entry.SerializedId, serializedId, StringComparison.Ordinal));

	/// <summary>Removes the chords from the entry of a command.</summary>
	/// <param name="serializedId">The serialized identifier of the entry to rewrite.</param>
	/// <param name="chords">The chords to remove from the entry.</param>
	/// <param name="codec">The codec that parses the entry's bindings.</param>
	/// <remarks>
	/// A catalog default is never rewritten: the proposal shadows it instead. Only the binding elements that declare a removed chord leave the entry,
	/// so an element that did not parse stays as stored. An entry that loses its last valid binding is deleted, its unparsed elements included,
	/// so the command falls back to its catalog defaults; the caller analyzes the candidate afterwards, because a restored default can collide
	/// with a binding another command holds. Any invalid stroke was reported when the overrides were loaded, so parsing the entry here logs nothing.
	/// </remarks>
	internal void RemoveChords(string serializedId, IReadOnlyList<KeyChord> chords, KeyBindingOverrideCodec codec)
	{
		KeyBindingOverrideEntry? entry = Find(serializedId);

		if (entry is null || entry.Bindings.Count == 0)
			return;

		List<KeyChord> remaining = codec.ParseEntryBindings(entry, serializedId, logDiagnostics: false);

		if (remaining.RemoveAll(chord => chords.Contains(chord)) == 0)
			return;

		if (remaining.Count == 0)
		{
			Entries.Remove(entry);
			return;
		}

		entry.Bindings.RemoveAll(binding => codec.ParseBinding(binding, serializedId, logDiagnostics: false) is { } chord && chords.Contains(chord));
	}
}
