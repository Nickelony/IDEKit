using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class CommandCatalogTests
{
	private static CommandDescriptor<TestCommand> CreateDescriptor(TestCommand command, params KeyChord[] bindings) =>
		new(command, serializedId: command.ToString(), remappingPolicy: CommandRemappingPolicy.Remappable, defaultBindings: bindings);

	[TestMethod]
	public void Constructor_ValidDescriptors_ExposesThem()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateDescriptor(TestCommand.Save, new KeyStroke(KeyCode.S, KeyModifiers.Control)),
			CreateDescriptor(TestCommand.Undo, new KeyStroke(KeyCode.Z, KeyModifiers.Control)),
		]);

		Assert.HasCount(2, catalog.Descriptors);
		Assert.IsTrue(catalog.TryGetDescriptor(TestCommand.Save, out CommandDescriptor<TestCommand>? descriptor));
		Assert.AreEqual(TestCommand.Save, descriptor.Command);
	}

	[TestMethod]
	public void Descriptors_ListsTheDescriptorsInTheOrderTheConstructorReceivedThem()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateDescriptor(TestCommand.Exit),
			CreateDescriptor(TestCommand.NewFile),
			CreateDescriptor(TestCommand.Save),
		]);

		Assert.AreSequenceEqual(
			[TestCommand.Exit, TestCommand.NewFile, TestCommand.Save],
			[.. catalog.Descriptors.Select(descriptor => descriptor.Command)]
		);
	}

	[TestMethod]
	public void Constructor_DefaultCommandValue_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([CreateDescriptor(default)]));

	[TestMethod]
	public void Constructor_DuplicateCommand_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(TestCommand.Save, "first", CommandRemappingPolicy.Remappable),
				new CommandDescriptor<TestCommand>(TestCommand.Save, "second", CommandRemappingPolicy.Remappable),
			])
		);
	}

	[TestMethod]
	public void Constructor_DuplicateSerializedId_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(TestCommand.Save, "editor.save", CommandRemappingPolicy.Remappable),
				new CommandDescriptor<TestCommand>(TestCommand.Undo, "editor.save", CommandRemappingPolicy.Remappable),
			])
		);
	}

	[TestMethod]
	public void Constructor_DuplicateDefaultBinding_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.Save,
					"editor.save",
					CommandRemappingPolicy.Remappable,
					new KeyStroke(KeyCode.S, KeyModifiers.Control)
				),

				new CommandDescriptor<TestCommand>(
					TestCommand.Undo,
					"editor.undo",
					CommandRemappingPolicy.Remappable,
					new KeyStroke(KeyCode.S, KeyModifiers.Control)
				),
			])
		);
	}

	[TestMethod]
	public void Constructor_NullEntry_Throws() => Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([null!]));

	[TestMethod]
	public void Constructor_PrefixOfAnotherDefaultBinding_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.Save,
					"editor.save",
					CommandRemappingPolicy.Remappable,
					new KeyStroke(KeyCode.K, KeyModifiers.Control)
				),

				new CommandDescriptor<TestCommand>(
					TestCommand.SaveAll,
					"editor.saveAll",
					CommandRemappingPolicy.Remappable,
					new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control))
				),
			])
		);
	}

	[TestMethod]
	public void Constructor_ChordsSharingAnUnboundPrefix_AreAccepted()
	{
		// Control+K is bound to nobody, so both chords can dispatch; sharing an unbound prefix is the
		// whole point of chords.
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				"editor.save",
				CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control))
			),

			new CommandDescriptor<TestCommand>(
				TestCommand.Find,
				"editor.find",
				CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.F, KeyModifiers.Control))
			),
		]);

		Assert.HasCount(2, catalog.Descriptors);
	}

	[TestMethod]
	public void TryGetDescriptor_UncatalogedCommand_ReturnsFalse()
	{
		var catalog = new CommandCatalog<TestCommand>([CreateDescriptor(TestCommand.Save, new KeyStroke(KeyCode.S, KeyModifiers.Control))]);

		Assert.IsFalse(catalog.TryGetDescriptor(TestCommand.None, out CommandDescriptor<TestCommand>? descriptor));
		Assert.IsNull(descriptor);
	}

	// ---- Contexts ----

	private static CommandDescriptor<TestCommand> CreateContextDescriptor(TestCommand command, string? context, params KeyChord[] bindings)
	{
		return new(command, serializedId: command.ToString(), remappingPolicy: CommandRemappingPolicy.Remappable, defaultBindings: bindings)
		{
			Context = context,
		};
	}

	[TestMethod]
	public void Constructor_SameChordInDifferentContexts_IsAccepted()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Build, "geometry", new KeyStroke(KeyCode.E, KeyModifiers.None)),
			CreateContextDescriptor(TestCommand.Find, "texture", new KeyStroke(KeyCode.E, KeyModifiers.None)),
		]);

		Assert.HasCount(2, catalog.Descriptors);
	}

	[TestMethod]
	public void Constructor_SameChordInTheSameContext_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				CreateContextDescriptor(TestCommand.Build, "geometry", new KeyStroke(KeyCode.E, KeyModifiers.None)),
				CreateContextDescriptor(TestCommand.Find, "geometry", new KeyStroke(KeyCode.E, KeyModifiers.None)),
			])
		);
	}

	[TestMethod]
	public void Constructor_SameChordAlwaysAndScoped_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				CreateContextDescriptor(TestCommand.Build, null, new KeyStroke(KeyCode.E, KeyModifiers.None)),
				CreateContextDescriptor(TestCommand.Find, "texture", new KeyStroke(KeyCode.E, KeyModifiers.None)),
			])
		);
	}

	[TestMethod]
	public void Constructor_ScopedChordAndAlwaysChordWithTheSameStroke_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				CreateContextDescriptor(TestCommand.Build, "texture", new KeyStroke(KeyCode.E, KeyModifiers.None)),
				CreateContextDescriptor(TestCommand.Find, null, new KeyStroke(KeyCode.E, KeyModifiers.None)),
			])
		);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public void Constructor_EmptyContextToken_Throws(string context)
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([CreateContextDescriptor(TestCommand.Build, context, new KeyStroke(KeyCode.E, KeyModifiers.None))])
		);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public void Constructor_EmptyTitle_Throws(string title)
	{
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable)
		{
			Title = title,
		};

		Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([descriptor]));
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public void Constructor_EmptyCategory_Throws(string category)
	{
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable)
		{
			Category = category,
		};

		Assert.ThrowsExactly<ArgumentException>(() => new CommandCatalog<TestCommand>([descriptor]));
	}

	[TestMethod]
	public void Constructor_TitleAndCategory_AreCatalogedWithTheDescriptor()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable)
			{
				Title = "Save File",
				Category = "File",
			},
		]);

		Assert.IsTrue(catalog.TryGetDescriptor(TestCommand.Save, out CommandDescriptor<TestCommand>? descriptor));
		Assert.AreEqual("Save File", descriptor.Title);
		Assert.AreEqual("File", descriptor.Category);
	}

	[TestMethod]
	public void Constructor_PrefixOfAChordInTheSameContext_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				CreateContextDescriptor(TestCommand.Save, "geometry", new KeyStroke(KeyCode.K, KeyModifiers.Control)),

				CreateContextDescriptor(
					TestCommand.SaveAll,
					"geometry",
					new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control))
				),
			])
		);
	}

	[TestMethod]
	public void Constructor_PrefixOfAChordInAnotherContext_IsAccepted()
	{
		var catalog = new CommandCatalog<TestCommand>([
			CreateContextDescriptor(TestCommand.Save, "geometry", new KeyStroke(KeyCode.K, KeyModifiers.Control)),

			CreateContextDescriptor(
				TestCommand.SaveAll,
				"texture",
				new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control))
			),
		]);

		Assert.HasCount(2, catalog.Descriptors);
	}

	[TestMethod]
	public void Constructor_PrefixOfAnAlwaysActiveChord_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				CreateContextDescriptor(TestCommand.Save, "geometry", new KeyStroke(KeyCode.K, KeyModifiers.Control)),

				CreateContextDescriptor(
					TestCommand.SaveAll,
					null,
					new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control))
				),
			])
		);
	}

	[TestMethod]
	public void Constructor_AlwaysActiveChordThatIsAPrefixOfAScopedChord_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandCatalog<TestCommand>([
				CreateContextDescriptor(TestCommand.Save, null, new KeyStroke(KeyCode.K, KeyModifiers.Control)),

				CreateContextDescriptor(
					TestCommand.SaveAll,
					"geometry",
					new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control))
				),
			])
		);
	}
}
