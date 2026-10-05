namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void SetText_AQueryThatLeavesNoItem_ShowsOneMessageRowWithTheEmptyTextOfTheProvider()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha", "beta");
		provider.EmptyText = "No matching names";
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.SetText("zzz");
		host.PassDebounce();

		Assert.HasCount(1, host.Controller.State.Rows);
		Assert.AreEqual("No matching names", host.Controller.State.Rows[0].Message);
		Assert.IsFalse(host.Controller.State.Rows[0].IsSelectable);
		Assert.IsNull(host.Controller.State.ActiveItem);
		Assert.AreEqual(-1, host.Controller.State.ActiveRowIndex);
	}

	[TestMethod]
	public void SetText_AProviderWithoutAnEmptyText_ShowsTheDefaultOfTheLibrary()
	{
		using var host = new QuickPickTestHost();
		var controller = new QuickPickController(Modes(new CountingPrefixProvider()), timeProvider: host.Dispatcher.TimeProvider);

		controller.Open("zzz");

		Assert.HasCount(1, controller.State.Rows);
		Assert.AreEqual("No matching results", controller.State.Rows[0].Message);

		controller.Dispose();
	}

	[TestMethod]
	public void AcceptAsync_OnTheMessageRow_DoesNothingAndKeepsTheSessionOpen()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("zzz");

		Assert.IsFalse(host.Accept());
		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.IsEmpty(provider.Accepted);
		Assert.IsFalse(host.Controller.MoveActive(1));
		Assert.IsFalse(host.Controller.SetActiveRow(0));
	}

	[TestMethod]
	public void SetText_AfterAnEmptyAnswer_ReplacesTheMessageRowWithTheItemsOfTheNextQuery()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha", "beta");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("zzz");
		Assert.AreEqual("No matching results", host.Controller.State.Rows[0].Message);

		host.Controller.SetText("alp");
		host.PassDebounce();

		Assert.AreSequenceEqual(["alpha"], host.Keys);
		Assert.IsNull(host.Controller.State.Rows[0].Message);
		Assert.AreEqual("alpha", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void Open_AnEmptyAnswerToAnEmptyQuery_ShowsNoMessageRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsEmpty(host.Controller.State.Rows);
	}

	[TestMethod]
	public void SetText_AQueryWhoseAnswerFails_ShowsNoMessageRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		provider.Answer = (_, _) => throw new InvalidOperationException("provider failed");
		host.Controller.SetText("zzz");
		host.PassDebounce();

		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.IsNotEmpty(host.Faults);
	}

	[TestMethod]
	public void SetText_TheProviderThrowsFromEmptyText_IsContainedAndShowsNoMessageRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");
		provider.EmptyTextFault = new InvalidOperationException("empty text failed");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("zzz");

		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.Contains(fault => fault.Message == "empty text failed", host.Faults);
	}

	[TestMethod]
	public void Open_AProviderThatFiltersItself_AnEmptyAnswerToAQueryShowsTheMessageRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");
		provider.FilterMode = QuickPickFilterMode.None;
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("anything");

		Assert.AreEqual("No matching results", host.Controller.State.Rows[0].Message);
	}

	[TestMethod]
	public void SetText_WordsInAnyOrder_FindTheSameItemAndHighlightEachWord()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "Format document", "Close tab");
		provider.FilterMode = QuickPickFilterMode.Words;
		using var host = new QuickPickTestHost(provider);

		foreach (string text in new[] { "format doc", "doc format" })
		{
			host.Controller.Open(text);

			Assert.AreSequenceEqual(["Format document"], host.Keys);
			Assert.AreSequenceEqual([new MatchRange(0, 6), new MatchRange(7, 3)], [.. host.Controller.State.ActiveItem!.LabelHighlights]);
		}
	}
}
