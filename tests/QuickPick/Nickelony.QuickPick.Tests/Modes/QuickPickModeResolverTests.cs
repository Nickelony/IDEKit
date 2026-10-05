namespace Nickelony.QuickPick.Tests;

/// <summary>Pins how the resolver picks a mode from the typed text and which sets of modes it refuses.</summary>
[TestClass]
public sealed class QuickPickModeResolverTests
{
	private static readonly QuickPickControllerOptions s_helpOn = QuickPickControllerOptions.Default;
	private static readonly QuickPickControllerOptions s_helpOff = QuickPickControllerOptions.Default with { EnableHelpMode = false };

	private static ScriptedQuickPickProvider Provider(string prefix) => new(prefix, name: $"Mode {prefix}");

	private static QuickPickMode[] Modes(params IQuickPickProvider[] providers) => [.. providers.Select(provider => new QuickPickMode(provider))];

	[TestMethod]
	public void Resolve_TheLongestPrefixThatTheTextStartsWith_WinsAndTheRestIsTheQuery()
	{
		ScriptedQuickPickProvider shorter = Provider("@");
		ScriptedQuickPickProvider longer = Provider("@:");

		var resolver = new QuickPickModeResolver(Modes(shorter, longer), s_helpOn);

		Assert.AreEqual((longer, "name"), resolver.Resolve("@:name"));
		Assert.AreEqual((shorter, "name"), resolver.Resolve("@name"));
	}

	[TestMethod]
	public void Resolve_TextWithNoPrefix_BelongsToTheModeWithTheEmptyPrefix()
	{
		ScriptedQuickPickProvider files = Provider("");
		ScriptedQuickPickProvider commands = Provider(">");

		var resolver = new QuickPickModeResolver(Modes(files, commands), s_helpOn);

		Assert.AreEqual((files, "util"), resolver.Resolve("util"));
		Assert.AreEqual((commands, "run"), resolver.Resolve(">run"));
	}

	[TestMethod]
	public void Resolve_TextWithNoPrefixAndNoDefaultMode_IsTheListOfModes()
	{
		var resolver = new QuickPickModeResolver(Modes(Provider(">")), s_helpOn);

		Assert.AreSame(resolver.Help, resolver.Resolve("util").Mode);
		Assert.AreSame(resolver.Help, resolver.Default);
	}

	[TestMethod]
	public void Resolve_TheReservedPrefixWhileTheHelpModeIsOn_IsTheListOfModes()
	{
		ScriptedQuickPickProvider files = Provider("");
		var resolver = new QuickPickModeResolver(Modes(files), s_helpOn);

		Assert.IsNotNull(resolver.Help);
		Assert.AreEqual((resolver.Help, "mode"), resolver.Resolve("?mode"));
	}

	[TestMethod]
	public void Resolve_TheReservedPrefixWhileTheHelpModeIsOff_IsPlainTextOfTheDefaultMode()
	{
		ScriptedQuickPickProvider commands = Provider("");
		var resolver = new QuickPickModeResolver(Modes(commands), s_helpOff);

		Assert.IsNull(resolver.Help);
		Assert.AreEqual((commands, "?mode"), resolver.Resolve("?mode"));
		Assert.AreEqual((commands, ">run"), resolver.Resolve(">run"));
	}

	[TestMethod]
	public void Constructor_TheReservedPrefixWhileTheHelpModeIsOff_PicksTheModeThatClaimsIt()
	{
		ScriptedQuickPickProvider files = Provider("");
		ScriptedQuickPickProvider questions = Provider("?");

		var resolver = new QuickPickModeResolver(Modes(files, questions), s_helpOff);

		Assert.AreEqual((questions, "why"), resolver.Resolve("?why"));
	}

	[TestMethod]
	public void Constructor_ANullEntryOrAModeWithoutAPrefix_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver([null!], s_helpOn));
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver(Modes(new PrefixlessProvider()), s_helpOn));
	}

	[TestMethod]
	public void Constructor_TwoModesWithOnePrefix_ThrowsWhetherTheyShareTheDefaultOrAnOverride()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver(Modes(Provider(">"), Provider(">")), s_helpOn));

		QuickPickMode[] overridden = [new(Provider(">")), new(Provider(":"), prefix: ">")];

		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver(overridden, s_helpOn));
	}

	[TestMethod]
	public void Constructor_OneProviderInTwoModes_Throws()
	{
		ScriptedQuickPickProvider provider = Provider(">");

		QuickPickMode[] twice = [new(provider), new(provider, prefix: ":")];

		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver(twice, s_helpOn));
	}

	[TestMethod]
	public void Constructor_TheReservedPrefixWhileTheHelpModeIsOn_Throws()
	{
		QuickPickMode[] overridden = [new(Provider(">"), prefix: "?")];

		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver(Modes(Provider("?")), s_helpOn));
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver(overridden, s_helpOn));
	}

	[TestMethod]
	public void Constructor_TheHelpModeOffAndNoModeWithTheEmptyPrefix_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver(Modes(Provider(">")), s_helpOff));
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickModeResolver([], s_helpOff));
	}

	[TestMethod]
	public void Constructor_NoModesWhileTheHelpModeIsOn_ListsNoModes()
	{
		var resolver = new QuickPickModeResolver([], s_helpOn);
		Assert.AreSame(resolver.Help, resolver.Default);
	}

	[TestMethod]
	public void PrefixOf_ReturnsTheOverrideOrElseThePrefixOfTheProvider()
	{
		ScriptedQuickPickProvider files = Provider("");
		ScriptedQuickPickProvider commands = Provider(">");
		ScriptedQuickPickProvider symbols = Provider("@");

		QuickPickMode[] modes = [new(files), new(commands, prefix: "!"), new(symbols)];

		var resolver = new QuickPickModeResolver(modes, s_helpOn);

		Assert.AreEqual(string.Empty, resolver.PrefixOf(files));
		Assert.AreEqual("!", resolver.PrefixOf(commands));
		Assert.AreEqual("@", resolver.PrefixOf(symbols));
		Assert.AreEqual("?", resolver.PrefixOf(resolver.Help!));
		Assert.AreEqual((commands, "run"), resolver.Resolve("!run"));
	}

	private sealed class PrefixlessProvider : IQuickPickProvider
	{
		public string Prefix => null!;

		public string Name => "Prefixless";

		public string Placeholder => string.Empty;

		public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<QuickPickItem>>([]);

		public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
