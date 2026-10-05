namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the <see cref="BindingsChangedEventArgs{TCommandId}"/> contract: a single-command change must name its command, while a whole-catalog
/// change may carry the identity's default value.
/// </summary>
[TestClass]
public sealed class BindingsChangedEventArgsTests
{
	[TestMethod]
	public void Constructor_NullCommandForASingleCommandChange_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new BindingsChangedEventArgs<string>(null));
		Assert.AreEqual("command", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_NamedCommand_ExposesTheCommand()
	{
		var args = new BindingsChangedEventArgs<string>("Save");

		Assert.AreEqual("Save", args.Command);
		Assert.IsFalse(args.AffectsAllCommands);
	}

	[TestMethod]
	public void Constructor_NullCommandForAllCommands_IsAccepted()
	{
		var args = new BindingsChangedEventArgs<string>(null, affectsAllCommands: true);

		Assert.IsNull(args.Command);
		Assert.IsTrue(args.AffectsAllCommands);
	}
}
