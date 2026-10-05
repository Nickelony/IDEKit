using Nickelony.IDEKit.LanguageFeatures.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class CodeActionCommandTests
{
	[TestMethod]
	public void Constructor_StoresTheTitleTheNameAndTheArguments()
	{
		var command = new CodeActionCommand("Remove spaces", "lua.removeSpace", ["file:///a.lua", 7, null]);

		Assert.AreEqual("Remove spaces", command.Title);
		Assert.AreEqual("lua.removeSpace", command.Name);
		Assert.AreSequenceEqual(["file:///a.lua", 7, null], command.Arguments);
	}

	[TestMethod]
	public void Constructor_WithoutArguments_HasAnEmptyList()
	{
		Assert.IsEmpty(new CodeActionCommand("Run", "lua.run").Arguments);
		Assert.IsEmpty(new CodeActionCommand("Run", "lua.run", []).Arguments);
	}

	[TestMethod]
	public void Constructor_CopiesTheArguments()
	{
		var arguments = new List<object?> { "first" };
		var command = new CodeActionCommand("Run", "lua.run", arguments);

		arguments.Add("second");

		Assert.HasCount(1, command.Arguments);
	}

	[TestMethod]
	public void Constructor_NullTitleOrName_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionCommand(null!, "lua.run"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionCommand("Run", null!));
	}

	[TestMethod]
	[DataRow("", "lua.run")]
	[DataRow("   ", "lua.run")]
	[DataRow("Run", "")]
	[DataRow("Run", "  ")]
	public void Constructor_BlankTitleOrName_Throws(string title, string name) =>
		Assert.ThrowsExactly<ArgumentException>(() => new CodeActionCommand(title, name));

	[TestMethod]
	public void Equality_ComparesTheArgumentsElementWise()
	{
		var command = new CodeActionCommand("Run", "lua.run", ["a", 1, null]);

		Assert.AreEqual(command, new CodeActionCommand("Run", "lua.run", ["a", 1, null]));
		Assert.AreEqual(command.GetHashCode(), new CodeActionCommand("Run", "lua.run", ["a", 1, null]).GetHashCode());
		Assert.AreNotEqual(command, new CodeActionCommand("Run", "lua.run", ["a", 2, null]));
		Assert.AreNotEqual(command, new CodeActionCommand("Run", "lua.run", ["a", 1]));
		Assert.AreNotEqual(command, new CodeActionCommand("Other", "lua.run", ["a", 1, null]));
		Assert.AreNotEqual(command, new CodeActionCommand("Run", "lua.other", ["a", 1, null]));
	}
}
