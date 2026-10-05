using System.Text.Json;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.KeyBindings.OverrideEntries;
using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class KeyBindingOverridesTests
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

	private static readonly KeyBindingOverrideCodec s_codec = new(NullLogger.Instance);

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
		KeyBindingOverrides loaded = JsonSerializer.Deserialize<KeyBindingOverrides>(json)!;

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
		Assert.AreEqual((int)KeyModifiers.Control, firstStroke.GetProperty("modifiers").GetInt32());

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
		KeyBindingOverrides loaded = JsonSerializer.Deserialize<KeyBindingOverrides>(ExampleJson)!;
		AssertExampleAppliesToService(loaded);
	}

	[TestMethod]
	public void Clone_IsDeepCopy()
	{
		var collection = new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion };

		collection.Entries.Add(
			new KeyBindingOverrideEntry
			{
				SerializedId = "editor.save",

				Bindings =
				[
					new KeyBindingOverrideChord { Strokes = [new KeyBindingOverrideStroke { KeyName = "S", Modifiers = (int)KeyModifiers.Control }] },
				],
			}
		);

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

	private static KeyBindingOverrides CreateExampleCollection()
	{
		var collection = new KeyBindingOverrides { Version = KeyBindingOverrides.CurrentVersion };

		collection.Entries.Add(
			new KeyBindingOverrideEntry
			{
				SerializedId = "editor.save",

				Bindings =
				[
					new KeyBindingOverrideChord
					{
						Strokes =
						[
							new KeyBindingOverrideStroke { KeyName = "K", Modifiers = (int)KeyModifiers.Control },
							new KeyBindingOverrideStroke { KeyName = "S", Modifiers = (int)KeyModifiers.Control },
						],
					},
				],
			}
		);

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
		Assert.AreEqual((int)KeyModifiers.Control, loaded.Entries[0].Bindings[0].Strokes[1].Modifiers);
		Assert.AreEqual("editor.undo", loaded.Entries[1].SerializedId);
		Assert.IsEmpty(loaded.Entries[1].Bindings);
	}

	private static void AssertExampleAppliesToService(KeyBindingOverrides loaded)
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				"Save",
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.F5, KeyModifiers.None)
			),
		]);

		var service = new KeyBindingService<TestCommand>(catalog, new RecordingOverrideStore(loaded));

		Assert.HasCount(1, service.GetBindings(TestCommand.Save));

		Assert.AreEqual(
			new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control)),
			service.GetBindings(TestCommand.Save)[0]
		);
	}

	[TestMethod]
	public void Find_MatchesTheSerializedIdOrdinally()
	{
		KeyBindingOverrides collection = CreateExampleCollection();

		Assert.AreSame(collection.Entries[0], collection.Find("editor.save"));
		Assert.IsNull(collection.Find("EDITOR.SAVE"));
		Assert.IsNull(collection.Find("editor.redo"));
	}

	[TestMethod]
	public void CloneWithout_DropsOnlyTheNamedEntryAndKeepsTheVersion()
	{
		KeyBindingOverrides collection = CreateExampleCollection();
		collection.Version = 7;

		KeyBindingOverrides clone = collection.CloneWithout("editor.save");

		Assert.AreEqual(7, clone.Version);
		Assert.HasCount(1, clone.Entries);
		Assert.AreEqual("editor.undo", clone.Entries[0].SerializedId);
		Assert.AreNotSame(collection.Entries[1], clone.Entries[0]);
		Assert.HasCount(2, collection.Entries);
	}

	[TestMethod]
	public void CloneWithout_MatchesTheSerializedIdOrdinally()
	{
		KeyBindingOverrides collection = CreateExampleCollection();

		KeyBindingOverrides clone = collection.CloneWithout("EDITOR.SAVE");

		Assert.HasCount(2, clone.Entries);
	}

	[TestMethod]
	public void CloneWhere_CopiesTheEntriesThePredicateKeeps()
	{
		KeyBindingOverrides collection = CreateExampleCollection();

		KeyBindingOverrides clone = collection.CloneWhere(entry => entry.Bindings.Count > 0);

		Assert.HasCount(1, clone.Entries);
		Assert.AreEqual("editor.save", clone.Entries[0].SerializedId);
		Assert.AreNotSame(collection.Entries[0], clone.Entries[0]);
	}

	[TestMethod]
	public void RemoveChords_RewritesTheEntryWithoutTheConflictingChord()
	{
		KeyChord save = new(new KeyStroke(KeyCode.S, KeyModifiers.Control));
		KeyChord open = new(new KeyStroke(KeyCode.O, KeyModifiers.Control));

		KeyBindingOverrides overrides = CreateSingleEntryOverrides("Save", save, open);

		overrides.RemoveChords("Save", [save], s_codec);

		KeyBindingOverrideEntry entry = overrides.Entries.Single();

		Assert.HasCount(1, entry.Bindings);
		Assert.AreEqual(open, s_codec.ParseBinding(entry.Bindings[0], "Save", logDiagnostics: false));
	}

	[TestMethod]
	public void RemoveChords_KeepsAnUnparsedBindingAsStored()
	{
		KeyChord save = new(new KeyStroke(KeyCode.S, KeyModifiers.Control));
		KeyChord open = new(new KeyStroke(KeyCode.O, KeyModifiers.Control));

		KeyBindingOverrides overrides = CreateSingleEntryOverrides("Save", save, open);
		KeyBindingOverrideEntry entry = overrides.Entries.Single();

		var unparsed = new KeyBindingOverrideChord();

		unparsed.Strokes.Add(new KeyBindingOverrideStroke { KeyName = "NotAKey", Modifiers = 0 });
		entry.Bindings.Insert(1, unparsed);

		overrides.RemoveChords("Save", [save], s_codec);

		Assert.HasCount(2, entry.Bindings);
		Assert.AreSame(unparsed, entry.Bindings[0]);
		Assert.AreEqual(open, s_codec.ParseBinding(entry.Bindings[1], "Save", logDiagnostics: false));
	}

	[TestMethod]
	public void RemoveChords_LastValidBinding_DeletesTheEntryWithItsUnparsedBindings()
	{
		KeyChord save = new(new KeyStroke(KeyCode.S, KeyModifiers.Control));

		KeyBindingOverrides overrides = CreateSingleEntryOverrides("Save", save);
		var unparsed = new KeyBindingOverrideChord();

		unparsed.Strokes.Add(new KeyBindingOverrideStroke { KeyName = "NotAKey", Modifiers = 0 });
		overrides.Entries[0].Bindings.Add(unparsed);

		overrides.RemoveChords("Save", [save], s_codec);

		Assert.IsEmpty(overrides.Entries);
	}

	[TestMethod]
	public void RemoveChords_LastBinding_DeletesTheEntry()
	{
		KeyChord save = new(new KeyStroke(KeyCode.S, KeyModifiers.Control));

		KeyBindingOverrides overrides = CreateSingleEntryOverrides("Save", save);

		overrides.RemoveChords("Save", [save], s_codec);

		Assert.IsEmpty(overrides.Entries);
	}

	[TestMethod]
	public void RemoveChords_NoMatchingChord_LeavesTheEntryUntouched()
	{
		KeyChord save = new(new KeyStroke(KeyCode.S, KeyModifiers.Control));
		KeyChord other = new(new KeyStroke(KeyCode.Q, KeyModifiers.Control));

		KeyBindingOverrides overrides = CreateSingleEntryOverrides("Save", save);

		KeyBindingOverrideEntry entry = overrides.Entries.Single();
		KeyBindingOverrideChord binding = entry.Bindings[0];

		overrides.RemoveChords("Save", [other], s_codec);

		Assert.AreSame(entry, overrides.Entries.Single());
		Assert.AreSame(binding, entry.Bindings.Single());
	}

	[TestMethod]
	public void RemoveChords_UnknownOrEmptyEntry_ChangesNothing()
	{
		KeyChord save = new(new KeyStroke(KeyCode.S, KeyModifiers.Control));

		KeyBindingOverrides overrides = CreateSingleEntryOverrides("Save", save);
		overrides.Entries.Add(new KeyBindingOverrideEntry { SerializedId = "Unbound" });

		overrides.RemoveChords("Missing", [save], s_codec);
		overrides.RemoveChords("Unbound", [save], s_codec);

		Assert.HasCount(2, overrides.Entries);
		Assert.HasCount(1, overrides.Entries[0].Bindings);
		Assert.IsEmpty(overrides.Entries[1].Bindings);
	}

	private static KeyBindingOverrides CreateSingleEntryOverrides(string serializedId, params KeyChord[] chords)
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(KeyBindingOverrideCodec.CreateEntry(serializedId, chords));

		return overrides;
	}
}
