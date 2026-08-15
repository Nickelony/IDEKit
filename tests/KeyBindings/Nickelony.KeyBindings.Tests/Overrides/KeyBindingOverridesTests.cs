using Nickelony.KeyBindings.Testing;
using System.Text.Json;
using System.Xml.Serialization;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class KeyBindingOverridesTests
{
	private const string ExampleXml = """
		<?xml version="1.0" encoding="utf-8"?>
		<KeyBindingOverrides Version="3">
		  <Command Id="Save">
		    <Binding>
		      <Stroke Key="K" Modifiers="2" />
		      <Stroke Key="S" Modifiers="2" />
		    </Binding>
		  </Command>
		</KeyBindingOverrides>
		""";

	private const string ExampleJson = """
		{
		  "version": 3,
		  "commands": [
		    { "id": "Save", "bindings": [ { "strokes": [ { "key": "K", "modifiers": 2 }, { "key": "S", "modifiers": 2 } ] } ] }
		  ]
		}
		""";

	[TestMethod]
	public void Defaults_AreCurrentVersionAndEmpty()
	{
		var collection = new KeyBindingOverrides();

		Assert.AreEqual(KeyBindingOverrides.CurrentVersion, collection.Version);
		Assert.IsNotNull(collection.Entries);
		Assert.IsEmpty(collection.Entries);
	}

	[TestMethod]
	public void XmlRoundTrip_PreservesAllValues()
	{
		var serializer = new XmlSerializer(typeof(KeyBindingOverrides));
		KeyBindingOverrides collection = CreateExampleCollection();

		using var writer = new StringWriter();
		serializer.Serialize(writer, collection);

		using var reader = new StringReader(writer.ToString());
		var loaded = (KeyBindingOverrides)serializer.Deserialize(reader)!;

		AssertExampleCollection(loaded);
	}

	[TestMethod]
	public void JsonRoundTrip_PreservesAllValues()
	{
		KeyBindingOverrides collection = CreateExampleCollection();

		string json = JsonSerializer.Serialize(collection);
		var loaded = JsonSerializer.Deserialize<KeyBindingOverrides>(json)!;

		AssertExampleCollection(loaded);
	}

	[TestMethod]
	public void JsonSerialize_UsesCanonicalMemberNames()
	{
		KeyBindingOverrides collection = CreateExampleCollection();

		string json = JsonSerializer.Serialize(collection);

		using var document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		Assert.AreEqual(KeyBindingOverrides.CurrentVersion, root.GetProperty("version").GetInt32());

		JsonElement firstCommand = root.GetProperty("commands")[0];
		Assert.AreEqual("editor.save", firstCommand.GetProperty("id").GetString());

		JsonElement firstBinding = firstCommand.GetProperty("bindings")[0];
		JsonElement firstStroke = firstBinding.GetProperty("strokes")[0];
		Assert.AreEqual("K", firstStroke.GetProperty("key").GetString());
		Assert.AreEqual((int)KeyModifierSet.Control, firstStroke.GetProperty("modifiers").GetInt32());

		Assert.AreEqual("editor.undo", root.GetProperty("commands")[1].GetProperty("id").GetString());
	}

	[TestMethod]
	public void Deserialize_ExampleXml_AppliesToService()
	{
		var serializer = new XmlSerializer(typeof(KeyBindingOverrides));
		using var reader = new StringReader(ExampleXml);

		var loaded = (KeyBindingOverrides)serializer.Deserialize(reader)!;

		AssertExampleAppliesToService(loaded);
	}

	[TestMethod]
	public void Deserialize_ExampleJson_AppliesToService()
	{
		var loaded = JsonSerializer.Deserialize<KeyBindingOverrides>(ExampleJson)!;

		AssertExampleAppliesToService(loaded);
	}

	[TestMethod]
	public void Clone_IsDeepCopy()
	{
		var collection = new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion };
		collection.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = "editor.save",
			Bindings =
			[
				new KeyBindingOverrideBinding
				{
					Strokes = [new KeyBindingOverrideStroke { KeyName = "S", Modifiers = (int)KeyModifierSet.Control }]
				}
			]
		});

		KeyBindingOverrides clone = collection.Clone();

		Assert.AreEqual(KeyBindingOverrides.CurrentVersion, clone.Version);
		Assert.HasCount(1, clone.Entries);
		Assert.AreNotSame(collection.Entries[0], clone.Entries[0]);
		Assert.AreNotSame(collection.Entries[0].Bindings[0], clone.Entries[0].Bindings[0]);
		Assert.AreNotSame(collection.Entries[0].Bindings[0].Strokes[0], clone.Entries[0].Bindings[0].Strokes[0]);
		Assert.AreEqual("S", clone.Entries[0].Bindings[0].Strokes[0].KeyName);

		collection.Entries[0].Bindings[0].Strokes[0].KeyName = "X";
		collection.Entries[0].Bindings.Clear();

		Assert.AreEqual("S", clone.Entries[0].Bindings[0].Strokes[0].KeyName);
		Assert.HasCount(1, clone.Entries[0].Bindings);
	}

	/// <summary>
	/// Pins that <c>Clone</c> is total: an instance whose lists a caller (or a deserializer) left unset is
	/// copied as an empty document instead of throwing a null reference exception.
	/// </summary>
	[TestMethod]
	public void Clone_UnsetCollections_AreTreatedAsEmpty()
	{
		var collection = new KeyBindingOverrides { Entries = null! };
		var entry = new KeyBindingOverrideEntry { SerializedId = "editor.save", Bindings = null! };
		var binding = new KeyBindingOverrideBinding { Strokes = null! };

		KeyBindingOverrides clonedCollection = collection.Clone();
		KeyBindingOverrideEntry clonedEntry = entry.Clone();
		KeyBindingOverrideBinding clonedBinding = binding.Clone();

		Assert.HasCount(0, clonedCollection.Entries);
		Assert.AreEqual("editor.save", clonedEntry.SerializedId);
		Assert.HasCount(0, clonedEntry.Bindings);
		Assert.HasCount(0, clonedBinding.Strokes);
	}

	private static KeyBindingOverrides CreateExampleCollection()
	{
		var collection = new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion };

		collection.Entries.Add(new KeyBindingOverrideEntry
		{
			SerializedId = "editor.save",
			Bindings =
			[
				new KeyBindingOverrideBinding
				{
					Strokes =
					[
						new KeyBindingOverrideStroke { KeyName = "K", Modifiers = (int)KeyModifierSet.Control },
						new KeyBindingOverrideStroke { KeyName = "S", Modifiers = (int)KeyModifierSet.Control }
					]
				}
			]
		});
		collection.Entries.Add(new KeyBindingOverrideEntry { SerializedId = "editor.undo" });

		return collection;
	}

	private static void AssertExampleCollection(KeyBindingOverrides loaded)
	{
		Assert.AreEqual(KeyBindingOverrides.CurrentVersion, loaded.Version);
		Assert.HasCount(2, loaded.Entries);
		Assert.AreEqual("editor.save", loaded.Entries[0].SerializedId);
		Assert.HasCount(1, loaded.Entries[0].Bindings);
		Assert.HasCount(2, loaded.Entries[0].Bindings[0].Strokes);
		Assert.AreEqual("K", loaded.Entries[0].Bindings[0].Strokes[0].KeyName);
		Assert.AreEqual((int)KeyModifierSet.Control, loaded.Entries[0].Bindings[0].Strokes[1].Modifiers);
		Assert.AreEqual("editor.undo", loaded.Entries[1].SerializedId);
		Assert.IsEmpty(loaded.Entries[1].Bindings);
	}

	private static void AssertExampleAppliesToService(KeyBindingOverrides loaded)
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "Save", CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.F5, KeyModifierSet.None))
		]);
		var service = new KeyBindingService<TestCommand>(catalog, new TestOverridesStore(loaded));

		Assert.HasCount(1, service.GetBindings(TestCommand.Save));
		Assert.AreEqual(
			new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			service.GetBindings(TestCommand.Save)[0]);
	}
}
