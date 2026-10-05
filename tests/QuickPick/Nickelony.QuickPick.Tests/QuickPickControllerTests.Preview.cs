namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void Open_PreviewsTheFirstItem()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.AreSequenceEqual(["preview:a"], provider.PreviewLog);
	}

	[TestMethod]
	public void MoveActive_PreviewsTheNewActiveItem()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b", "c");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);
		host.Controller.SetActiveRow(2);

		Assert.AreSequenceEqual(["preview:a", "preview:b", "preview:c"], provider.PreviewLog);
	}

	[TestMethod]
	public void Dismiss_AfterAPreview_RestoresTheEditorOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);

		host.Controller.Dismiss();
		host.Controller.Dismiss();

		Assert.AreSequenceEqual(["preview:a", "preview:b", "cancel"], provider.PreviewLog);
		Assert.IsEmpty(provider.Accepted);
	}

	[TestMethod]
	public void Dismiss_WithoutAPreview_AsksForNoRestore()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.Dismiss();

		Assert.IsEmpty(provider.PreviewLog);
	}

	[TestMethod]
	public void AcceptAsync_AfterAPreview_KeepsThePreviewAndNeverRestoresIt()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);

		Assert.IsTrue(host.Accept());

		Assert.AreSequenceEqual(["preview:a", "preview:b"], provider.PreviewLog);
		Assert.AreEqual("b", provider.Accepted[0].Key);
	}

	[TestMethod]
	public void Open_WhileASessionIsOpen_RestoresThePreviewOfTheEarlierSession()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.Open();

		Assert.AreSequenceEqual(["preview:a", "cancel", "preview:a"], provider.PreviewLog);
	}

	[TestMethod]
	public void SetText_ToAnotherMode_RestoresThePreviewOfTheOldMode()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a");
		ScriptedQuickPickProvider symbols = ProviderOf("@", "outline");

		using var host = new QuickPickTestHost(files, symbols);

		host.Controller.Open();
		host.Controller.SetText("@");
		host.PassDebounce();

		Assert.AreSequenceEqual(["preview:a", "cancel"], files.PreviewLog);
		Assert.AreSequenceEqual(["preview:outline"], symbols.PreviewLog);
	}

	[TestMethod]
	public void Query_AnAnswerWithoutAnActiveItem_RestoresThePreview()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha", "beta");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.SetText("zzz");
		host.PassDebounce();

		Assert.AreSequenceEqual(["preview:alpha", "cancel"], provider.PreviewLog);
	}

	[TestMethod]
	public void Refresh_TheActiveItemStays_DoesNotPreviewItAgain()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);
		host.Controller.Refresh();

		Assert.AreSequenceEqual(["preview:a", "preview:b"], provider.PreviewLog);
	}

	[TestMethod]
	public void Dispose_AfterAPreview_RestoresTheEditor()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.Dispose();

		Assert.AreSequenceEqual(["preview:a", "cancel"], provider.PreviewLog);
	}

	[TestMethod]
	public void Preview_Throws_IsContainedAndReported()
	{
		var provider = new ThrowingPreviewProvider();
		using var host = new QuickPickTestHost();

		QuickPickController controller = new(Modes(provider), reportFault: host.Faults.Add, timeProvider: host.Dispatcher.TimeProvider);

		controller.Open();
		controller.Dismiss();

		Assert.HasCount(2, host.Faults);
		controller.Dispose();
	}

	private sealed class ThrowingPreviewProvider : IQuickPickProvider
	{
		public string Prefix => string.Empty;

		public string Name => "Throwing";

		public string Placeholder => string.Empty;

		public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<QuickPickItem>>([new QuickPickItem("a", "a")]);

		public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public void Preview(QuickPickItem item) => throw new InvalidOperationException("preview failed");

		public void CancelPreview() => throw new InvalidOperationException("cancel failed");
	}
}
