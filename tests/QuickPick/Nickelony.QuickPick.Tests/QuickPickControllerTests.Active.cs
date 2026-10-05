namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void Open_ActivatesTheFirstSelectableRow()
	{
		var group = new QuickPickGroup("g", "Group");

		ScriptedQuickPickProvider provider = ProviderOf("");

		provider.Items.Add(ScriptedQuickPickProvider.Item("off", group, isEnabled: false));
		provider.Items.Add(ScriptedQuickPickProvider.Item("on", group));

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		// Row 0 is the separator, row 1 the disabled item and row 2 the first item that can be active.
		Assert.AreEqual(2, host.Controller.State.ActiveRowIndex);
		Assert.AreEqual("on", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void Open_NoItemCanBeActive_LeavesNoActiveRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");
		provider.Items.Add(ScriptedQuickPickProvider.Item("off", isEnabled: false));
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.AreEqual(-1, host.Controller.State.ActiveRowIndex);
		Assert.IsNull(host.Controller.State.ActiveItem);
	}

	[TestMethod]
	public void MoveActive_SkipsSeparatorsAndDisabledItems()
	{
		var first = new QuickPickGroup("one", "One");
		var second = new QuickPickGroup("two", "Two");

		ScriptedQuickPickProvider provider = ProviderOf("");

		provider.Items.AddRange([
			ScriptedQuickPickProvider.Item("a", first),
			ScriptedQuickPickProvider.Item("off", first, isEnabled: false),
			ScriptedQuickPickProvider.Item("b", second),
		]);

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.AreEqual("a", host.Controller.State.ActiveItem!.Key);
		Assert.IsTrue(host.Controller.MoveActive(1));
		Assert.AreEqual("b", host.Controller.State.ActiveItem!.Key);
		Assert.IsTrue(host.Controller.MoveActive(-1));
		Assert.AreEqual("a", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void MoveActive_PastTheEnd_WrapsToTheOtherEnd()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b", "c");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsTrue(host.Controller.MoveActive(-1));
		Assert.AreEqual("c", host.Controller.State.ActiveItem!.Key);
		Assert.IsTrue(host.Controller.MoveActive(1));
		Assert.AreEqual("a", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void MoveActive_WithoutWrapping_StopsAtTheEnd()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b", "c");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsTrue(host.Controller.MoveActive(10, wrap: false));
		Assert.AreEqual("c", host.Controller.State.ActiveItem!.Key);
		Assert.IsFalse(host.Controller.MoveActive(10, wrap: false));
		Assert.IsTrue(host.Controller.MoveActive(-10, wrap: false));
		Assert.AreEqual("a", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void MoveActive_NothingToMoveTo_ReturnsFalse()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "only");
		using var host = new QuickPickTestHost(provider);

		Assert.IsFalse(host.Controller.MoveActive(1));

		host.Controller.Open();

		Assert.IsFalse(host.Controller.MoveActive(1));
		Assert.IsFalse(host.Controller.MoveActive(0));
		Assert.AreEqual("only", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void SetActiveRow_AnEnabledItem_BecomesActive()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsTrue(host.Controller.SetActiveRow(1));
		Assert.AreEqual("b", host.Controller.State.ActiveItem!.Key);
		Assert.IsFalse(host.Controller.SetActiveRow(1));
	}

	[TestMethod]
	public void SetActiveRow_ASeparatorADisabledItemOrNoRow_ChangesNothing()
	{
		var group = new QuickPickGroup("g", "Group");

		ScriptedQuickPickProvider provider = ProviderOf("");

		provider.Items.Add(ScriptedQuickPickProvider.Item("a", group));
		provider.Items.Add(ScriptedQuickPickProvider.Item("off", group, isEnabled: false));

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		int active = host.Controller.State.ActiveRowIndex;

		Assert.IsFalse(host.Controller.SetActiveRow(0));
		Assert.IsFalse(host.Controller.SetActiveRow(2));
		Assert.IsFalse(host.Controller.SetActiveRow(-1));
		Assert.IsFalse(host.Controller.SetActiveRow(9));
		Assert.AreEqual(active, host.Controller.State.ActiveRowIndex);
	}

	[TestMethod]
	public void SetText_AfterMovingTheActiveRow_ActivatesTheFirstRowOfTheNewAnswer()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a1", "a2", "a3");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(2);
		Assert.AreEqual("a3", host.Controller.State.ActiveItem!.Key);

		host.Controller.SetText("a");
		host.PassDebounce();

		Assert.AreEqual("a1", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void Refresh_TheNewAnswerHoldsTheActiveKey_KeepsTheActiveItem()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b", "c");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);
		Assert.AreEqual("b", host.Controller.State.ActiveItem!.Key);

		// A new item lands before the active one, so the active row moves while the active item stays.
		provider.Items.Insert(0, ScriptedQuickPickProvider.Item("new"));
		host.Controller.Refresh();

		Assert.AreEqual("b", host.Controller.State.ActiveItem!.Key);
		Assert.AreEqual(2, host.Controller.State.ActiveRowIndex);
	}

	[TestMethod]
	public void Refresh_TheNewAnswerLacksTheActiveKey_ActivatesTheFirstRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b", "c");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);
		provider.Items.RemoveAt(1);
		host.Controller.Refresh();

		Assert.AreEqual("a", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void Refresh_TheActiveItemIsNowDisabled_ActivatesTheFirstRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);
		provider.Items[1] = ScriptedQuickPickProvider.Item("b", isEnabled: false);
		host.Controller.Refresh();

		Assert.AreEqual("a", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void Refresh_WhenClosed_DoesNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Refresh();

		Assert.IsEmpty(provider.Requests);
	}
}
