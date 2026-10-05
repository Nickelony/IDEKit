namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void Dismiss_TellsTheModeItIsDeactivatedAfterThePreviewWasCanceled()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");

		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Controller.Dismiss();

		Assert.AreSequenceEqual([0], provider.DeactivationLog);
		Assert.AreSequenceEqual(["preview:alpha", "cancel"], provider.PreviewLog);
	}

	[TestMethod]
	public void Dismiss_WithoutAnOpenSession_DeactivatesNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");

		using var host = new QuickPickTestHost(provider);

		host.Controller.Dismiss();

		Assert.IsEmpty(provider.DeactivationLog);
	}

	[TestMethod]
	public void AcceptAsync_DeactivatesTheModeBeforeTheProviderAccepts()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");

		using var host = new QuickPickTestHost(provider);

		host.Open();

		Assert.IsTrue(host.Accept());
		Assert.AreSequenceEqual([0], provider.DeactivationLog);
		Assert.HasCount(1, provider.Accepted);
	}

	[TestMethod]
	public void SetText_WithinOneMode_DoesNotDeactivateIt()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha", "beta");

		using var host = new QuickPickTestHost(provider);

		host.Open();

		host.Type("al");
		host.Type("alp");

		Assert.IsEmpty(provider.DeactivationLog);
	}

	[TestMethod]
	public void SetText_ToAnotherMode_DeactivatesTheModeItLeft()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "alpha");
		ScriptedQuickPickProvider commands = ProviderOf(">", "run");

		using var host = new QuickPickTestHost(files, commands);

		host.Open();
		host.Controller.SetText(">");

		Assert.AreSequenceEqual([0], files.DeactivationLog);
		Assert.IsEmpty(commands.DeactivationLog);

		host.Controller.Dismiss();

		Assert.HasCount(1, files.DeactivationLog);
		Assert.HasCount(1, commands.DeactivationLog);
	}

	[TestMethod]
	public void Open_WhileASessionIsOpen_DeactivatesTheModeOfTheEndedSession()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");

		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Controller.Open();

		Assert.HasCount(1, provider.DeactivationLog);
	}

	[TestMethod]
	public void Dispose_DeactivatesTheModeOfTheOpenSession()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");
		var host = new QuickPickTestHost(provider);

		host.Open();
		host.Dispose();

		Assert.HasCount(1, provider.DeactivationLog);
	}

	[TestMethod]
	public void Dismiss_AModeWhoseDeactivationThrows_IsContainedAndReported()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");

		provider.DeactivationFault = new InvalidOperationException("release failed");

		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Controller.Dismiss();

		Assert.IsFalse(host.Controller.State.IsOpen);
		Assert.AreEqual("release failed", host.Faults.Single().Message);
	}

	[TestMethod]
	public void SetText_AModeWithItsOwnQueryDelay_StartsTheQueryAfterThatDelayAndNotAfterTheDelayOfTheController()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");

		provider.QueryDelay = TimeSpan.FromMilliseconds(200);

		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Controller.SetText("a");
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.QueryDebounceDelay);

		Assert.HasCount(1, provider.Requests);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(149));

		Assert.HasCount(1, provider.Requests);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1));

		Assert.HasCount(2, provider.Requests);
	}

	[TestMethod]
	public void SetText_AModeWithoutADelayOfItsOwn_UsesTheDelayOfTheController()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha");

		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Controller.SetText("a");
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.QueryDebounceDelay);

		Assert.HasCount(2, provider.Requests);
	}

	[TestMethod]
	public void SetText_AModeWhoseDelayIsNegativeOrThrows_UsesTheDelayOfTheController()
	{
		ScriptedQuickPickProvider negative = ProviderOf("", "alpha");

		negative.QueryDelay = TimeSpan.FromMilliseconds(-5);

		using (var host = new QuickPickTestHost(negative))
		{
			host.Open();
			host.Controller.SetText("a");
			host.Dispatcher.Advance(QuickPickControllerOptions.Default.QueryDebounceDelay);

			Assert.HasCount(2, negative.Requests);
		}

		ScriptedQuickPickProvider throwing = ProviderOf("", "alpha");

		throwing.QueryDelayFault = new InvalidOperationException("no delay");

		using (var host = new QuickPickTestHost(throwing))
		{
			host.Open();
			host.Controller.SetText("a");
			host.Dispatcher.Advance(QuickPickControllerOptions.Default.QueryDebounceDelay);

			Assert.HasCount(2, throwing.Requests);
			Assert.IsNotEmpty(host.Faults);
		}
	}

	[TestMethod]
	public void SetText_ReadsTheDelayOfTheModeThatTheTextSelects()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "alpha");
		ScriptedQuickPickProvider symbols = ProviderOf("#", "sym");

		symbols.QueryDelay = TimeSpan.FromMilliseconds(200);

		using var host = new QuickPickTestHost(files, symbols);

		host.Open("#");
		host.Controller.SetText("#s");
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.QueryDebounceDelay);

		Assert.HasCount(1, symbols.Requests);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(150));

		Assert.HasCount(2, symbols.Requests);
	}
}
