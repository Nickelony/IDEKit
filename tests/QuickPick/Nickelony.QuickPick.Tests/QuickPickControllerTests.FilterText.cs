namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	private static string BeforeTheColon(string query) => query.Split(':')[0];

	[TestMethod]
	public void Open_AProviderThatSplitsItsQuery_RanksByTheFilterPartOnlyAndAsksWithTheWholeQuery()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha.cs", "beta.cs");

		provider.FilterText = BeforeTheColon;

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("alpha.cs:12");

		Assert.AreSequenceEqual(["alpha.cs"], host.Keys);
		Assert.AreEqual("alpha.cs:12", provider.Requests[0].Query);
	}

	[TestMethod]
	public void SetText_AProviderThatSplitsItsQuery_RanksEachTypedTextByItsFilterPart()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha.cs", "beta.cs");

		provider.FilterText = BeforeTheColon;

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("alpha");
		host.Controller.SetText("beta.cs:3:4");
		host.PassDebounce();

		Assert.AreSequenceEqual(["beta.cs"], host.Keys);
	}

	[TestMethod]
	public void Open_AProviderThatSplitsItsQueryAndMatchesNothing_ShowsTheEmptyRow()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha.cs");

		provider.FilterText = BeforeTheColon;

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("zzz:12");

		Assert.AreEqual("No matching results", host.Controller.State.Rows.Single().Message);
	}

	[TestMethod]
	public void Open_AProviderWhoseFilterTextThrows_RanksByTheWholeQueryAndReportsTheFault()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha.cs", "beta.cs");

		provider.FilterText = _ => throw new InvalidOperationException("split failed");

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("beta");

		Assert.AreSequenceEqual(["beta.cs"], host.Keys);
		Assert.IsInstanceOfType<InvalidOperationException>(host.Faults.Single());
	}

	[TestMethod]
	public void AcceptAsync_PassesTheTextThatWasTypedToTheProvider()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha.cs", "beta.cs");

		provider.FilterText = BeforeTheColon;

		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("alpha");
		host.Controller.SetText("beta.cs:7:3");

		Assert.IsTrue(host.Accept());

		Assert.AreEqual("beta.cs", provider.Accepted.Single().Key);
		Assert.AreSequenceEqual(["beta.cs:7:3"], provider.AcceptedQueries);
	}

	[TestMethod]
	public void AcceptAsync_WithNothingTyped_PassesAnEmptyText()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "save");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open(">");

		Assert.IsTrue(host.Accept());
		Assert.AreSequenceEqual([string.Empty], provider.AcceptedQueries);
	}

	[TestMethod]
	public void AcceptAsync_ThePrefixIsNotPartOfTheTextThatWasTyped()
	{
		ScriptedQuickPickProvider provider = ProviderOf("edt ", "tab");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("edt ta");

		Assert.IsTrue(host.Accept());
		Assert.AreSequenceEqual(["ta"], provider.AcceptedQueries);
	}
}
