#if AVALONIAEDIT
using Avalonia.Controls;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Controls;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Covers a hover controller from the factory that shows its hovers through a presenter over a shown editor.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class HoverPresenterScenarioTests
{
	private const string Text = "first line\nsecond line";

	[TestMethod]
	public void TypingUnderARestingPointer_KeepsTheHoverHidden()
	{
		using var host = new PopupTestHost(Text);
		using var presenter = new HoverPresenter(host.Editor.TextArea, static (_, _) => new TextBlock { Text = "content" });
		var provider = new CountingHoverProvider();

		using HoverController controller = HoverControllerFactory.Create(
			textArea: host.Editor.TextArea,
			provider,

			hooks: new HoverControllerHooks
			{
				BuildRequest = static offset => new HoverRequest("scenario.lua", Text, offset),
				ShowHover = presenter.Show,
			},

			options: new HoverPointerOptions { RestDelay = TimeSpan.FromMilliseconds(10) },
			logger: null,
			offsetFromPoint: static _ => 2
		);

		TestHoverPointerEvents.RaiseMoved(host.Editor.TextArea);
		DispatcherTestHelpers.PumpUntil(() => presenter.IsOpen);

		host.Editor.TextArea.PerformTextInput("x");

		Assert.IsFalse(presenter.IsOpen);

		// A platform reports a move to the same position when the closing popup uncovers the text area, and the pointer did not move.
		TestHoverPointerEvents.RaiseMoved(host.Editor.TextArea);
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(150));

		Assert.IsFalse(presenter.IsOpen);
		Assert.AreEqual(1, provider.RequestCount);
	}

	private sealed class CountingHoverProvider : IHoverProvider
	{
		public int RequestCount { get; private set; }

		public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
		{
			RequestCount++;
			return Task.FromResult<HoverInformation?>(new HoverInformation("hover"));
		}
	}
}
