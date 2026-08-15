using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class CommandCatalogTests
{
	private static CommandDescriptor<TestCommand> CreateDescriptor(TestCommand command, params KeyChord[] bindings)
		=> new(command, command.ToString(), CommandRemappingPolicy.Remappable, bindings);

	[TestMethod]
	public void Constructor_ValidDescriptors_ExposesThem()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateDescriptor(TestCommand.Save, new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			CreateDescriptor(TestCommand.Undo, new KeyCombo(KeyCode.Z, KeyModifierSet.Control))
		]);

		Assert.HasCount(2, catalog.Descriptors);
		Assert.IsTrue(catalog.TryGetDescriptor(TestCommand.Save, out CommandDescriptor<TestCommand>? descriptor));
		Assert.AreEqual(TestCommand.Save, descriptor.Command);
	}

	[TestMethod]
	public void Constructor_DefaultCommandValue_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateDescriptor(default)
		]));

	[TestMethod]
	public void Constructor_DuplicateCommand_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "first", CommandRemappingPolicy.Remappable),
			new CommandDescriptor<TestCommand>(TestCommand.Save, "second", CommandRemappingPolicy.Remappable)
		]));

	[TestMethod]
	public void Constructor_DuplicateSerializedId_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "editor.save", CommandRemappingPolicy.Remappable),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, "editor.save", CommandRemappingPolicy.Remappable)
		]));

	[TestMethod]
	public void Constructor_DuplicateDefaultBinding_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "editor.save", CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, "editor.undo", CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.S, KeyModifierSet.Control))
		]));

	[TestMethod]
	public void Constructor_NullEntry_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([null!]));

	[TestMethod]
	public void Constructor_PrefixOfAnotherDefaultBinding_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "editor.save", CommandRemappingPolicy.Remappable,
				new KeyCombo(KeyCode.K, KeyModifierSet.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, "editor.saveAll", CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)))
		]));

	[TestMethod]
	public void Constructor_ChordsSharingAnUnboundPrefix_AreAccepted()
	{
		// Control+K is bound to nobody, so both chords can dispatch; sharing an unbound prefix is the
		// whole point of chords.
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "editor.save", CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control))),
			new CommandDescriptor<TestCommand>(TestCommand.Find, "editor.find", CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.F, KeyModifierSet.Control)))
		]);

		Assert.HasCount(2, catalog.Descriptors);
	}

	[TestMethod]
	public void TryGetDescriptor_UncatalogedCommand_ReturnsFalse()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateDescriptor(TestCommand.Save, new KeyCombo(KeyCode.S, KeyModifierSet.Control))
		]);

		Assert.IsFalse(catalog.TryGetDescriptor(TestCommand.None, out CommandDescriptor<TestCommand>? descriptor));
		Assert.IsNull(descriptor);
	}

	// ---- Contexts ----

	private static CommandDescriptor<TestCommand> CreateContextDescriptor(TestCommand command, string? context, params KeyChord[] bindings)
		=> new(command, command.ToString(), CommandRemappingPolicy.Remappable, bindings) { Context = context };

	[TestMethod]
	public void Constructor_SameChordInDifferentContexts_IsAccepted()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Build, "geometry", new KeyCombo(KeyCode.E, KeyModifierSet.None)),
			CreateContextDescriptor(TestCommand.Find, "texture", new KeyCombo(KeyCode.E, KeyModifierSet.None))
		]);

		Assert.HasCount(2, catalog.Descriptors);
	}

	[TestMethod]
	public void Constructor_SameChordInTheSameContext_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Build, "geometry", new KeyCombo(KeyCode.E, KeyModifierSet.None)),
			CreateContextDescriptor(TestCommand.Find, "geometry", new KeyCombo(KeyCode.E, KeyModifierSet.None))
		]));

	[TestMethod]
	public void Constructor_SameChordAlwaysAndScoped_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Build, null, new KeyCombo(KeyCode.E, KeyModifierSet.None)),
			CreateContextDescriptor(TestCommand.Find, "texture", new KeyCombo(KeyCode.E, KeyModifierSet.None))
		]));

	[TestMethod]
	public void Constructor_ScopedChordAndAlwaysChordWithTheSameCombo_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Build, "texture", new KeyCombo(KeyCode.E, KeyModifierSet.None)),
			CreateContextDescriptor(TestCommand.Find, null, new KeyCombo(KeyCode.E, KeyModifierSet.None))
		]));

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public void Constructor_EmptyContextToken_Throws(string context)
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Build, context, new KeyCombo(KeyCode.E, KeyModifierSet.None))
		]));

	[TestMethod]
	public void Constructor_PrefixOfAChordInTheSameContext_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Save, "geometry", new KeyCombo(KeyCode.K, KeyModifierSet.Control)),
			CreateContextDescriptor(TestCommand.SaveAll, "geometry",
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)))
		]));

	[TestMethod]
	public void Constructor_PrefixOfAChordInAnotherContext_IsAccepted()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Save, "geometry", new KeyCombo(KeyCode.K, KeyModifierSet.Control)),
			CreateContextDescriptor(TestCommand.SaveAll, "texture",
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)))
		]);

		Assert.HasCount(2, catalog.Descriptors);
	}

	[TestMethod]
	public void Constructor_PrefixOfAnAlwaysActiveChord_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Save, "geometry", new KeyCombo(KeyCode.K, KeyModifierSet.Control)),
			CreateContextDescriptor(TestCommand.SaveAll, null,
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)))
		]));

	[TestMethod]
	public void Constructor_AlwaysActiveChordThatIsAPrefixOfAScopedChord_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Save, null, new KeyCombo(KeyCode.K, KeyModifierSet.Control)),
			CreateContextDescriptor(TestCommand.SaveAll, "geometry",
				new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)))
		]));
}
