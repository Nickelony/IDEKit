using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Nickelony.KeyBindings;

/// <summary>
/// Collection of key binding overrides with a schema version, serializable as XML or JSON.
/// A host can include an instance in its settings document.
/// </summary>
/// <remarks>
/// <para>
/// An override entry replaces the catalog defaults of its command: the command claims exactly the
/// entry's bindings, and an empty binding list explicitly unbinds it. In the schema a binding is one
/// <see cref="KeyChord"/> and a stroke is one <see cref="KeyCombo"/> of that chord, which is why the
/// persisted types are named <see cref="KeyBindingOverrideBinding"/> and
/// <see cref="KeyBindingOverrideStroke"/>.
/// </para>
/// <para>
/// <see cref="CurrentVersion"/> is the only schema version this package reads or writes; a document
/// with any other version is rejected instead of being tolerated.
/// </para>
/// <para>
/// <see cref="Entries"/> and the nested binding and stroke collections are mutable
/// <see cref="List{T}"/> properties with public setters, so the model can round-trip through
/// <c>System.Xml.Serialization</c> and <c>System.Text.Json</c>. That is the deliberate exception to the
/// package's rule that a public payload collection is typed as
/// <c>IReadOnlyList&lt;T&gt;</c>: an immutable shape would make the serializer unable to populate the
/// instance.
/// </para>
/// <para>
/// The package README describes override precedence, the schema, and the version <c>3</c> shape in full.
/// </para>
/// </remarks>
/// <example>
/// <code><![CDATA[
/// <KeyBindingOverrides Version="3">
///   <Command Id="Save">
///     <Binding>
///       <Stroke Key="K" Modifiers="2" />
///       <Stroke Key="S" Modifiers="2" />
///     </Binding>
///   </Command>
/// </KeyBindingOverrides>
/// ]]></code>
/// </example>
public sealed class KeyBindingOverrides
{
	/// <summary>
	/// The schema version written and read by this package.
	/// </summary>
	public const int CurrentVersion = 3;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingOverrides"/> class.
	/// </summary>
	public KeyBindingOverrides()
	{
		Version = CurrentVersion;
		Entries = [];
	}

	/// <summary>
	/// Gets or sets the schema version written as the <c>Version</c> XML attribute and the
	/// <c>version</c> JSON member. New collections start at <see cref="CurrentVersion"/>, which is
	/// also the only value the service accepts.
	/// </summary>
	[XmlAttribute("Version")]
	[JsonPropertyName("version")]
	public int Version { get; set; }

	/// <summary>
	/// Gets or sets the per-command override entries.
	/// </summary>
	[XmlElement("Command")]
	[JsonPropertyName("commands")]
	public List<KeyBindingOverrideEntry> Entries { get; set; }

	/// <summary>
	/// Creates a deep copy of this collection, including every entry and binding.
	/// </summary>
	/// <remarks>
	/// A list a caller left unset (<see langword="null"/>, the state a partially populated instance can
	/// be in) is treated as empty and a <see langword="null"/> element is skipped, so the copy is always
	/// usable.
	/// </remarks>
	/// <returns>The copied collection with the same <see cref="Version"/> as this instance.</returns>
	public KeyBindingOverrides Clone()
	{
		var clone = new KeyBindingOverrides { Version = Version };

		if (Entries is null)
			return clone;

		foreach (KeyBindingOverrideEntry entry in Entries)
		{
			if (entry is not null)
				clone.Entries.Add(entry.Clone());
		}

		return clone;
	}
}

/// <summary>
/// A single command override entry in the persisted key binding settings.
/// </summary>
/// <remarks>
/// <see cref="Bindings"/> is a mutable <see cref="List{T}"/> property with a public setter so the entry
/// can round-trip through the serializers; see <see cref="KeyBindingOverrides"/> for the exception to
/// the <c>IReadOnlyList&lt;T&gt;</c> payload rule.
/// </remarks>
public sealed class KeyBindingOverrideEntry
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingOverrideEntry"/> class.
	/// </summary>
	public KeyBindingOverrideEntry()
	{
		SerializedId = string.Empty;
		Bindings = [];
	}

	/// <summary>
	/// Gets or sets the stable serialized identifier of the catalog descriptor this entry overrides.
	/// </summary>
	/// <remarks>
	/// The value is matched ordinally (case-sensitive) against
	/// <see cref="CommandDescriptor{TCommandId}.SerializedId"/>.
	/// </remarks>
	[XmlAttribute("Id")]
	[JsonPropertyName("id")]
	public string SerializedId { get; set; }

	/// <summary>
	/// Gets or sets the binding entries for this command.
	/// </summary>
	/// <remarks>
	/// An empty list explicitly unbinds the command; repeated chords in one entry are ignored after
	/// the first.
	/// </remarks>
	[XmlElement("Binding")]
	[JsonPropertyName("bindings")]
	public List<KeyBindingOverrideBinding> Bindings { get; set; }

	/// <summary>
	/// Creates a deep copy of this entry, including every binding.
	/// </summary>
	/// <remarks>
	/// A list a caller left unset (<see langword="null"/>) is treated as empty and a
	/// <see langword="null"/> element is skipped, so the copy is always usable.
	/// </remarks>
	/// <returns>The copied entry.</returns>
	public KeyBindingOverrideEntry Clone()
	{
		var clone = new KeyBindingOverrideEntry { SerializedId = SerializedId };

		if (Bindings is null)
			return clone;

		foreach (KeyBindingOverrideBinding binding in Bindings)
		{
			if (binding is not null)
				clone.Bindings.Add(binding.Clone());
		}

		return clone;
	}
}
