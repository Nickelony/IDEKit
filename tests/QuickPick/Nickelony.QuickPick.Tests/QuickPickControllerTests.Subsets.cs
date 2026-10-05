namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	private static readonly QuickPickControllerOptions s_withoutHelp = QuickPickControllerOptions.Default with { EnableHelpMode = false };

	[TestMethod]
	public void Open_ACommandPaletteAlone_FiltersTheCommandAndHelpCharactersAsPlainText()
	{
		ScriptedQuickPickProvider commands = ProviderOf(string.Empty, "Save File", "What is this?", "> prompt");

		using var host = QuickPickTestHost.ForModes(new QuickPickMode(commands, prefix: string.Empty));

		host.CreateController(s_withoutHelp);
		host.Open("?");

		Assert.AreEqual("?", commands.Requests[^1].Query);
		Assert.AreEqual(commands.Placeholder, host.Controller.State.Placeholder);
		Assert.AreSequenceEqual(["What is this?"], [.. host.Labels]);

		host.Type(">");

		Assert.AreEqual(">", commands.Requests[^1].Query);
		Assert.AreEqual(commands.Placeholder, host.Controller.State.Placeholder);
		Assert.AreSequenceEqual(["> prompt"], [.. host.Labels]);
	}

	[TestMethod]
	public void Accept_ACommandPaletteAlone_RunsTheCommandOfTheActiveRow()
	{
		ScriptedQuickPickProvider commands = ProviderOf(string.Empty, "Save File", "Close All");

		using var host = QuickPickTestHost.ForModes(new QuickPickMode(commands, prefix: string.Empty));

		host.CreateController(s_withoutHelp);
		host.Open();
		host.Type("close");

		Assert.IsTrue(host.Accept());
		Assert.AreSequenceEqual(["Close All"], [.. commands.Accepted.Select(item => item.Key)]);
		Assert.IsFalse(host.Controller.State.IsOpen);
	}

	[TestMethod]
	public void Open_FilesAndCommands_PicksTheModeByThePrefixItWasOfferedUnder()
	{
		ScriptedQuickPickProvider files = ProviderOf(string.Empty, "util.lua");
		ScriptedQuickPickProvider commands = ProviderOf(">", "Run");

		using var host = new QuickPickTestHost(files, commands);

		host.Open();

		Assert.HasCount(1, files.Requests);
		Assert.IsEmpty(commands.Requests);

		host.Open(">ru");

		Assert.AreEqual("ru", commands.Requests[^1].Query);
		Assert.AreEqual(commands.Placeholder, host.Controller.State.Placeholder);
	}

	[TestMethod]
	public void Open_APrefixOverride_ReplacesTheDefaultPrefixOfTheProvider()
	{
		ScriptedQuickPickProvider files = ProviderOf(string.Empty, "util.lua");
		ScriptedQuickPickProvider commands = ProviderOf(">", "Run");

		using var host = QuickPickTestHost.ForModes(new QuickPickMode(files), new QuickPickMode(commands, prefix: ":"));

		host.Open(":ru");

		Assert.AreEqual("ru", commands.Requests[^1].Query);

		host.Open(">ru");

		// The provider keeps ">" as its default prefix only, so the text now belongs to the mode with the empty prefix.
		Assert.AreEqual(">ru", files.Requests[^1].Query);
		Assert.HasCount(1, commands.Requests);
	}

	[TestMethod]
	public void Open_TheIdeSetup_ListsEveryModeUnderItsPrefixInTheHelpMode()
	{
		QuickPickMode[] modes =
		[
			new(ProviderOf(string.Empty, "util.lua")),
			new(ProviderOf(">", "Run")),
			new(ProviderOf("edt ", "tab")),
			new(ProviderOf("@", "symbol"), prefix: "!"),
		];

		using var host = QuickPickTestHost.ForModes(modes);

		host.Open("?");

		Assert.AreSequenceEqual([string.Empty, ">", "edt ", "!"], [.. host.Keys]);

		Assert.AreSequenceEqual(
			[null, ">", "edt", "!"],
			[.. host.Controller.State.Rows.Where(row => row.Item is not null).Select(row => row.Item!.TrailingText)]
		);
	}

	[TestMethod]
	public void Open_TheTextsOfTheListOfModes_ComeFromTheOptionsOfTheController()
	{
		QuickPickMode[] modes = [new(ProviderOf(string.Empty, "util.lua")), new(ProviderOf(">", "Run"))];

		using var host = QuickPickTestHost.ForModes(modes);

		host.Open("?");

		Assert.AreEqual(QuickPickControllerOptions.Default.HelpModePlaceholder, host.Controller.State.Placeholder);

		host.Type("?zzz");

		Assert.AreSequenceEqual([$"!{QuickPickControllerOptions.Default.HelpModeEmptyText}"], [.. host.RowTexts]);

		host.CreateController(
			QuickPickControllerOptions.Default with
			{
				HelpModePlaceholder = "Modus waehlen",
				HelpModeEmptyText = "Kein Modus gefunden",
			}
		);

		host.Open("?");

		Assert.AreEqual("Modus waehlen", host.Controller.State.Placeholder);

		host.Type("?zzz");

		Assert.AreSequenceEqual(["!Kein Modus gefunden"], [.. host.RowTexts]);
	}

	[TestMethod]
	public void Accept_ARowOfTheListOfModes_TypesThePrefixTheModeWasOfferedUnder()
	{
		ScriptedQuickPickProvider symbols = ProviderOf("@", "symbol");

		using var host = QuickPickTestHost.ForModes(new QuickPickMode(ProviderOf(string.Empty, "util.lua")), new QuickPickMode(symbols, prefix: "!"));

		host.Open("?");
		host.Controller.MoveActive(1);

		Assert.IsTrue(host.Accept());
		Assert.AreEqual("!", host.Controller.State.Text);
		Assert.IsTrue(host.Controller.State.IsOpen);
	}

	[TestMethod]
	public void Open_TheReservedPrefixWhileTheHelpModeIsOff_PicksTheModeThatClaimsIt()
	{
		ScriptedQuickPickProvider files = ProviderOf(string.Empty, "util.lua");
		ScriptedQuickPickProvider questions = ProviderOf("?", "why");

		using var host = new QuickPickTestHost(files, questions);

		host.CreateController(s_withoutHelp);
		host.Open("?wh");

		Assert.AreEqual("wh", questions.Requests[^1].Query);
		Assert.IsEmpty(files.Requests);
	}

	[TestMethod]
	public void AcceptAsync_ARecentItem_IsKeptUnderThePrefixTheModeWasOfferedUnder()
	{
		ScriptedQuickPickProvider commands = ProviderOf(">", "Run");

		commands.RecentGroup = new QuickPickGroup("recent", "recent");

		var store = new RecordingQuickPickRecentStore();

		using var host = QuickPickTestHost.ForModes(
			new QuickPickMode(ProviderOf(string.Empty, "util.lua")),
			new QuickPickMode(commands, prefix: "!")
		);

		host.CreateController(recentStore: store);
		host.Open("!");

		Assert.IsTrue(host.Accept());
		Assert.AreSequenceEqual(["Run"], [.. store.KeysOf("!")]);
		Assert.IsEmpty(store.KeysOf(">"));
	}

	[TestMethod]
	public void Constructor_TheHelpModeOffAndNoModeWithTheEmptyPrefix_Throws()
	{
		using var host = new QuickPickTestHost(ProviderOf(">", "Run"));
		Assert.ThrowsExactly<ArgumentException>(() => host.CreateController(s_withoutHelp));
	}
}
