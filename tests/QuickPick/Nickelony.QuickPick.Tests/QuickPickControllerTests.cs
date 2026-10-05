namespace Nickelony.QuickPick.Tests;

/// <summary>
/// Covers how the quick-pick controller is built and how it picks the provider of a typed text: the prefix routing, the longest prefix,
/// the list of modes, and the validation of the modes it is given.
/// </summary>
[TestClass]
public sealed partial class QuickPickControllerTests
{
	private static ScriptedQuickPickProvider ProviderOf(string prefix, params string[] keys)
	{
		var provider = new ScriptedQuickPickProvider(prefix, name: prefix.Length == 0 ? "Default" : $"Mode {prefix.Trim()}");

		provider.Items.AddRange(keys.Select(key => ScriptedQuickPickProvider.Item(key)));

		return provider;
	}

	private static QuickPickMode[] Modes(params IQuickPickProvider[] providers) => [.. providers.Select(provider => new QuickPickMode(provider))];

	[TestMethod]
	public void Constructor_NullModes_Throws()
	{
		using var host = new QuickPickTestHost();
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickController(null!));
	}

	[TestMethod]
	public void Constructor_NullMode_Throws()
	{
		using var host = new QuickPickTestHost();
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickController([null!]));
	}

	[TestMethod]
	public void Constructor_TwoProvidersWithOnePrefix_Throws()
	{
		using var host = new QuickPickTestHost();

		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickController(Modes(ProviderOf(">"), ProviderOf(">"))));
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickController(Modes(ProviderOf(""), ProviderOf(""))));
	}

	[TestMethod]
	public void Constructor_ReservedPrefix_Throws()
	{
		using var host = new QuickPickTestHost();
		Assert.ThrowsExactly<ArgumentException>(() => new QuickPickController(Modes(ProviderOf("?"))));
	}

	[TestMethod]
	public void Constructor_WithoutASynchronizationContext_Throws()
	{
		using var thread = new FakePumpedUiThread(installSynchronizationContext: false);

		Assert.IsNull(SynchronizationContext.Current);
		Assert.ThrowsExactly<InvalidOperationException>(() => new QuickPickController(Modes(ProviderOf(""))));
	}

	[TestMethod]
	public void Constructor_OnlyTheBaseSynchronizationContext_Throws()
	{
		SynchronizationContext? previous = SynchronizationContext.Current;

		SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

		try
		{
			// The base type posts to the thread pool, so a controller that took it would run its queries and its publications on pool threads.
			Assert.ThrowsExactly<InvalidOperationException>(() => new QuickPickController(Modes(ProviderOf(""))));
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}
	}

	[TestMethod]
	public void Constructor_OnAnotherThread_MakesThatThreadTheOwner()
	{
		using var host = new QuickPickTestHost();
		QuickPickController? controller = null;

		var thread = new Thread(() =>
		{
			SynchronizationContext.SetSynchronizationContext(host.Dispatcher.Context);
			controller = new QuickPickController(Modes(ProviderOf("")));
		});

		thread.Start();
		thread.Join();

		Assert.IsNotNull(controller);
		Assert.ThrowsExactly<InvalidOperationException>(() => controller.Open());
	}

	[TestMethod]
	public void Open_WithoutAPrefix_AsksTheDefaultProviderAtOnce()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt", "b.txt");
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");

		using var host = new QuickPickTestHost(files, commands);

		host.Controller.Open();

		Assert.HasCount(1, files.Requests);
		Assert.AreEqual(string.Empty, files.Requests[0].Query);
		Assert.IsEmpty(commands.Requests);
		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.AreSequenceEqual(["a.txt", "b.txt"], host.Keys);
		Assert.AreEqual(files.Placeholder, host.Controller.State.Placeholder);
	}

	[TestMethod]
	public void Open_WithAPrefix_AsksTheProviderOfThePrefixForTheTextThatFollowsIt()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		ScriptedQuickPickProvider commands = ProviderOf(">", "format");

		using var host = new QuickPickTestHost(files, commands);

		host.Controller.Open(">for");

		Assert.IsEmpty(files.Requests);
		Assert.HasCount(1, commands.Requests);
		Assert.AreEqual("for", commands.Requests[0].Query);
		Assert.AreEqual(">for", host.Controller.State.Text);
		Assert.AreEqual(commands.Placeholder, host.Controller.State.Placeholder);
		Assert.AreSequenceEqual(["format"], host.Keys);
	}

	[TestMethod]
	public void SetText_TypingAPrefix_SwitchesToTheModeOfThePrefix()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");

		using var host = new QuickPickTestHost(files, commands);

		host.Controller.Open();
		host.Controller.SetText(">");
		host.PassDebounce();

		Assert.AreSequenceEqual(["save"], host.Keys);
		Assert.AreEqual(string.Empty, commands.Requests[^1].Query);
		Assert.AreEqual(commands.Placeholder, host.Controller.State.Placeholder);
	}

	[TestMethod]
	public void SetText_TwoPrefixesWhereOneExtendsTheOther_PicksTheLongerPrefix()
	{
		ScriptedQuickPickProvider symbols = ProviderOf("@", "outline");
		ScriptedQuickPickProvider grouped = ProviderOf("@:", "by kind");

		using var host = new QuickPickTestHost(symbols, grouped);

		host.Controller.Open("@:fn");

		Assert.IsEmpty(symbols.Requests);
		Assert.AreEqual("fn", grouped.Requests[0].Query);

		host.Controller.SetText("@fn");
		host.PassDebounce();

		Assert.AreEqual("fn", symbols.Requests[^1].Query);
	}

	[TestMethod]
	public void SetText_APrefixWithATrailingSpace_Routes()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		ScriptedQuickPickProvider editors = ProviderOf("edt ", "tab");

		using var host = new QuickPickTestHost(files, editors);

		host.Controller.Open("edt ");

		Assert.AreEqual(string.Empty, editors.Requests[0].Query);
		Assert.IsEmpty(files.Requests);

		host.Controller.SetText("edt");
		host.PassDebounce();

		// Without the space, the text belongs to the default mode.
		Assert.AreEqual("edt", files.Requests[0].Query);
	}

	[TestMethod]
	public void SetText_TheDefaultModeText_IsPassedWholeAsTheQuery()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");

		using var host = new QuickPickTestHost(files, commands);

		host.Controller.Open();
		host.Controller.SetText("src/main");
		host.PassDebounce();

		Assert.AreEqual("src/main", files.Requests[^1].Query);
	}

	[TestMethod]
	public void SetText_NoModeClaimsTheText_ListsTheModes()
	{
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");
		ScriptedQuickPickProvider symbols = ProviderOf("@", "outline");

		using var host = new QuickPickTestHost(commands, symbols);

		host.Controller.Open();

		Assert.IsEmpty(commands.Requests);
		Assert.AreSequenceEqual([">", "@"], host.Keys);
		Assert.AreEqual("Select a mode", host.Controller.State.Placeholder);
	}

	[TestMethod]
	public void Open_QuestionMark_ListsEveryModeWithItsPrefix()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");

		using var host = new QuickPickTestHost(files, commands);

		host.Controller.Open("?");

		Assert.AreSequenceEqual(["Default", "Mode >"], host.RowTexts);

		QuickPickItem[] items = [.. host.Controller.State.Rows.Select(row => row.Item!)];

		Assert.IsNull(items[0].TrailingText);
		Assert.AreEqual(">", items[1].TrailingText);
		Assert.AreEqual(commands.Placeholder, items[1].Description);
		Assert.IsEmpty(files.Requests);
		Assert.IsEmpty(commands.Requests);
	}

	[TestMethod]
	public void Open_QuestionMarkWithAQuery_FiltersTheModes()
	{
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");
		ScriptedQuickPickProvider symbols = ProviderOf("@", "outline");

		using var host = new QuickPickTestHost(commands, symbols);

		host.Controller.Open("?@");

		// Only the name of the symbol mode holds an at sign.
		Assert.AreSequenceEqual(["@"], host.Keys);
	}

	[TestMethod]
	public void AcceptAsync_OnAModeOfTheList_TypesThePrefixOfTheModeAndStaysOpen()
	{
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");
		ScriptedQuickPickProvider symbols = ProviderOf("@", "outline");

		using var host = new QuickPickTestHost(commands, symbols);

		host.Controller.Open("?");
		Assert.IsTrue(host.Controller.MoveActive(1));

		Assert.IsTrue(host.Accept());
		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.AreEqual("@", host.Controller.State.Text);
		Assert.AreSequenceEqual(["outline"], host.Keys);
		Assert.IsEmpty(commands.Accepted);
		Assert.IsEmpty(symbols.Accepted);
	}

	[TestMethod]
	public void SetText_ChangeOfMode_ClearsTheRowsOfTheOldModeAtOnce()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		ScriptedQuickPickProvider commands = ProviderOf(">", "save");

		using var host = new QuickPickTestHost(files, commands);

		host.Controller.Open();
		host.Controller.SetText(">");

		// The debounce has not elapsed, yet the rows of the file mode are gone.
		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.AreEqual(-1, host.Controller.State.ActiveRowIndex);
	}

	[TestMethod]
	public void SetText_SameText_DoesNotQueryAgain()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		using var host = new QuickPickTestHost(files);

		host.Controller.Open("a");
		host.Controller.SetText("a");
		host.PassDebounce();

		Assert.HasCount(1, files.Requests);
	}

	[TestMethod]
	public void SetText_WhenClosed_DoesNothing()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a.txt");
		using var host = new QuickPickTestHost(files);

		host.Controller.SetText("a");
		host.PassDebounce();

		Assert.IsEmpty(files.Requests);
		Assert.IsFalse(host.Controller.State.IsOpen);
	}
}
