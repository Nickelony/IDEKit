using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class CommandDescriptorTests
{
	[TestMethod]
	public void Constructor_CopiesDefaultBindings()
	{
		KeyChord[] bindings = [new KeyStroke(KeyCode.S, KeyModifiers.Control)];

		var descriptor = new CommandDescriptor<TestCommand>(
			command: TestCommand.Save,
			serializedId: nameof(TestCommand.Save),
			remappingPolicy: CommandRemappingPolicy.Remappable,
			defaultBindings: bindings
		);

		bindings[0] = new KeyStroke(KeyCode.X, KeyModifiers.None);

		Assert.HasCount(1, descriptor.DefaultBindings);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), descriptor.DefaultBindings[0]);
	}

	[TestMethod]
	public void Constructor_ChordDefaultBindings_AreStoredInOrder()
	{
		var descriptor = new CommandDescriptor<TestCommand>(
			TestCommand.SaveAll,
			nameof(TestCommand.SaveAll),
			CommandRemappingPolicy.Remappable,
			new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control))
		);

		Assert.HasCount(1, descriptor.DefaultBindings);
		Assert.AreEqual(2, descriptor.DefaultBindings[0].StrokeCount);
		Assert.AreEqual(new KeyStroke(KeyCode.K, KeyModifiers.Control), descriptor.DefaultBindings[0].Strokes[0]);
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), descriptor.DefaultBindings[0].Strokes[1]);
	}

	[TestMethod]
	public void Constructor_WithoutDefaultBindings_UsesEmptyList()
	{
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable);
		Assert.IsEmpty(descriptor.DefaultBindings);
	}

	[TestMethod]
	public void Constructor_DuplicateDefaultBindings_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandDescriptor<TestCommand>(
				TestCommand.Save,
				nameof(TestCommand.Save),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(KeyCode.S, KeyModifiers.Control),
				new KeyStroke(KeyCode.S, KeyModifiers.Control)
			)
		);
	}

	[TestMethod]
	public void Constructor_UninitializedDefaultBinding_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable, default(KeyStroke))
		);
	}

	[TestMethod]
	public void Constructor_UndefinedRemappingPolicy_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), (CommandRemappingPolicy)7)
		);
	}

	[TestMethod]
	public void Constructor_WhitespaceSerializedId_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			new CommandDescriptor<TestCommand>(TestCommand.Save, "   ", CommandRemappingPolicy.Remappable)
		);

		Assert.AreEqual("serializedId", exception.ParamName);
	}

	[TestMethod]
	public void Properties_ExposeConstructorArguments()
	{
		var descriptor = new CommandDescriptor<TestCommand>(
			TestCommand.Exit,
			"editor.exit",
			CommandRemappingPolicy.HostReserved,
			new KeyStroke(KeyCode.F4, KeyModifiers.Alt)
		);

		Assert.AreEqual(TestCommand.Exit, descriptor.Command);
		Assert.AreEqual("editor.exit", descriptor.SerializedId);
		Assert.AreEqual(CommandRemappingPolicy.HostReserved, descriptor.RemappingPolicy);
		Assert.HasCount(1, descriptor.DefaultBindings);
	}

	[TestMethod]
	public void Context_WithoutAToken_IsAlwaysActive()
	{
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable);
		Assert.IsNull(descriptor.Context);
	}

	[TestMethod]
	public void Context_WithAToken_IsStored()
	{
		var descriptor = new CommandDescriptor<TestCommand>(
			TestCommand.Build,
			nameof(TestCommand.Build),
			CommandRemappingPolicy.Remappable,
			new KeyStroke(KeyCode.E, KeyModifiers.None)
		)
		{
			Context = "geometry",
		};

		Assert.AreEqual("geometry", descriptor.Context);
	}

	[TestMethod]
	public void TitleAndCategory_WithoutValues_AreNull()
	{
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable);

		Assert.IsNull(descriptor.Title);
		Assert.IsNull(descriptor.Category);
	}

	[TestMethod]
	public void TitleAndCategory_WithValues_AreStored()
	{
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable)
		{
			Title = "Save File",
			Category = "File",
		};

		Assert.AreEqual("Save File", descriptor.Title);
		Assert.AreEqual("File", descriptor.Category);
	}
}
