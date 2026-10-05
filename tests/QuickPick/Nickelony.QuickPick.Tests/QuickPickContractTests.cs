namespace Nickelony.QuickPick.Tests;

/// <summary>Pins the guards and the defaults of the quick-pick item, group and request records.</summary>
[TestClass]
public sealed class QuickPickContractTests
{
	[TestMethod]
	public void Item_WithOnlyAKeyAndALabel_IsEnabledAndHasNoDecoration()
	{
		var item = new QuickPickItem("file.save", "Save File");

		Assert.AreEqual("file.save", item.Key);
		Assert.AreEqual("Save File", item.Label);
		Assert.IsTrue(item.IsEnabled);
		Assert.IsNull(item.Description);
		Assert.IsNull(item.Detail);
		Assert.IsNull(item.IconKey);
		Assert.IsNull(item.TrailingText);
		Assert.IsNull(item.Group);
		Assert.IsNull(item.Tag);
		Assert.IsEmpty(item.LabelHighlights);
		Assert.IsEmpty(item.DescriptionHighlights);
	}

	[TestMethod]
	public void Item_WithHighlights_KeepsTheKeyAndTheLabel()
	{
		var item = new QuickPickItem("a", "Alpha");
		QuickPickItem highlighted = item with { LabelHighlights = [new MatchRange(0, 2)] };

		Assert.AreEqual("a", highlighted.Key);
		Assert.AreEqual("Alpha", highlighted.Label);
		Assert.AreSequenceEqual([new MatchRange(0, 2)], [.. highlighted.LabelHighlights]);
		Assert.IsEmpty(item.LabelHighlights);
	}

	[TestMethod]
	public void Item_NullKey_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickItem(null!, "label"));

	[TestMethod]
	public void Item_NullLabel_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickItem("key", null!));

	[TestMethod]
	public void Group_WithoutALabel_DrawsNoSeparator()
	{
		var group = new QuickPickGroup("recent");

		Assert.AreEqual("recent", group.Id);
		Assert.IsNull(group.Label);
		Assert.IsFalse(group.ShowsCount);
	}

	[TestMethod]
	public void Group_NullId_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickGroup(null!));

	[TestMethod]
	public void Request_KeepsTheQueryAndTheRecentKeys()
	{
		var request = new QuickPickRequest("fmt", ["a", "b"]);

		Assert.AreEqual("fmt", request.Query);
		Assert.AreSequenceEqual(["a", "b"], [.. request.RecentKeys]);
	}

	[TestMethod]
	public void Request_NullArgument_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickRequest(null!, []));
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickRequest("", null!));
	}

	[TestMethod]
	public void Group_Order_DefaultsToZeroAndKeepsTheGivenValue()
	{
		Assert.AreEqual(0, new QuickPickGroup("a").Order);
		Assert.AreEqual(-3, new QuickPickGroup("a", order: -3).Order);
	}

	[TestMethod]
	public void Item_NullHighlights_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickItem("a", "a") { LabelHighlights = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickItem("a", "a") { DescriptionHighlights = null! });
	}

	[TestMethod]
	public void Item_Tag_CarriesTheObjectTheProviderAttached()
	{
		object command = new();
		Assert.AreSame(command, new QuickPickItem("a", "a") { Tag = command }.Tag);
	}

	[TestMethod]
	public void PresentationState_NullTextPlaceholderOrRows_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickPresentationState { Text = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickPresentationState { Placeholder = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickPresentationState { Rows = null! });
	}

	[TestMethod]
	public void PresentationState_ActiveItem_IsTheItemOfTheActiveRow()
	{
		var item = new QuickPickItem("a", "a");
		var state = new QuickPickPresentationState { Rows = [QuickPickRow.ForSeparator("s"), QuickPickRow.ForItem(item)], ActiveRowIndex = 1 };

		Assert.AreSame(item, state.ActiveItem);
		Assert.IsNull((state with { ActiveRowIndex = 0 }).ActiveItem);
		Assert.IsNull((state with { ActiveRowIndex = 5 }).ActiveItem);
	}

	[TestMethod]
	public void Row_Separator_CarriesNoItemAndIsNotSelectable()
	{
		var separator = QuickPickRow.ForSeparator("Group");

		Assert.IsNull(separator.Item);
		Assert.AreEqual("Group", separator.SeparatorLabel);
		Assert.IsFalse(separator.IsSelectable);
		Assert.IsTrue(QuickPickRow.ForItem(new QuickPickItem("a", "a")).IsSelectable);
		Assert.IsFalse(QuickPickRow.ForItem(new QuickPickItem("a", "a") { IsEnabled = false }).IsSelectable);
	}

	[TestMethod]
	public void Row_NullArgument_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => QuickPickRow.ForItem(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => QuickPickRow.ForSeparator(null!));
	}

	[TestMethod]
	public void Options_TheHelpMode_IsOnByDefaultAndTheDelaysRefuseANegativeValue()
	{
		Assert.IsTrue(QuickPickControllerOptions.Default.EnableHelpMode);
		Assert.IsFalse((QuickPickControllerOptions.Default with { EnableHelpMode = false }).EnableHelpMode);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			QuickPickControllerOptions.Default with
			{
				QueryDebounceDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			QuickPickControllerOptions.Default with
			{
				BusyDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);
	}

	[TestMethod]
	public void Options_TheTextsOfTheListOfModes_AreNotEmptyByDefaultAndRefuseNull()
	{
		Assert.IsFalse(string.IsNullOrWhiteSpace(QuickPickControllerOptions.Default.HelpModePlaceholder));
		Assert.IsFalse(string.IsNullOrWhiteSpace(QuickPickControllerOptions.Default.HelpModeEmptyText));
		Assert.ThrowsExactly<ArgumentNullException>(() => QuickPickControllerOptions.Default with { HelpModePlaceholder = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => QuickPickControllerOptions.Default with { HelpModeEmptyText = null! });
	}

	[TestMethod]
	public void Options_InvalidValues_Throw()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => QuickPickControllerOptions.Default with { MaxItemCount = 0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => QuickPickControllerOptions.Default with { MaxRecentCount = 0 });

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			QuickPickControllerOptions.Default with
			{
				QueryDebounceDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			QuickPickControllerOptions.Default with
			{
				BusyDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);
	}

	[TestMethod]
	public void Match_NullRanges_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new FuzzyMatch(1, null!, []));
		Assert.ThrowsExactly<ArgumentNullException>(() => new FuzzyMatch(1, [], null!));
	}
}
