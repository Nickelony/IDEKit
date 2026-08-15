using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class CommandDescriptorTests
{
	[TestMethod]
	public void Constructor_CopiesDefaultBindings()
	{
		KeyChord[] bindings = [new KeyCombo(KeyCode.S, KeyModifierSet.Control)];

		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable, bindings);

		bindings[0] = new KeyCombo(KeyCode.X, KeyModifierSet.None);

		Assert.HasCount(1, descriptor.DefaultBindings);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), descriptor.DefaultBindings[0]);
	}

	[TestMethod]
	public void Constructor_ChordDefaultBindings_AreStoredInOrder()
	{
		var descriptor = new CommandDescriptor<TestCommand>(
			TestCommand.SaveAll,
			nameof(TestCommand.SaveAll),
			CommandRemappingPolicy.Remappable,
			new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control)));

		Assert.HasCount(1, descriptor.DefaultBindings);
		Assert.AreEqual(2, descriptor.DefaultBindings[0].StrokeCount);
		Assert.AreEqual(new KeyCombo(KeyCode.K, KeyModifierSet.Control), descriptor.DefaultBindings[0].Strokes[0]);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), descriptor.DefaultBindings[0].Strokes[1]);
	}

	[TestMethod]
	public void Constructor_WithoutDefaultBindings_UsesEmptyList()
	{
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), CommandRemappingPolicy.Remappable);

		Assert.IsEmpty(descriptor.DefaultBindings);
	}

	[TestMethod]
	public void Constructor_DuplicateDefaultBindings_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandDescriptor<TestCommand>(
			TestCommand.Save,
			nameof(TestCommand.Save),
			CommandRemappingPolicy.Remappable,
			new KeyCombo(KeyCode.S, KeyModifierSet.Control),
			new KeyCombo(KeyCode.S, KeyModifierSet.Control)));

	[TestMethod]
	public void Constructor_UninitializedDefaultBinding_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandDescriptor<TestCommand>(
			TestCommand.Save,
			nameof(TestCommand.Save),
			CommandRemappingPolicy.Remappable,
			default(KeyCombo)));

	[TestMethod]
	public void Constructor_UndefinedRemappingPolicy_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new CommandDescriptor<TestCommand>(
			TestCommand.Save,
			nameof(TestCommand.Save),
			(CommandRemappingPolicy)7));

	[TestMethod]
	public void Properties_ExposeConstructorArguments()
	{
		var descriptor = new CommandDescriptor<TestCommand>(
			TestCommand.Exit,
			"editor.exit",
			CommandRemappingPolicy.HostReserved,
			new KeyCombo(KeyCode.F4, KeyModifierSet.Alt));

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
		var descriptor = new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
			new KeyCombo(KeyCode.E, KeyModifierSet.None))
		{ Context = "geometry" };

		Assert.AreEqual("geometry", descriptor.Context);
	}
}
