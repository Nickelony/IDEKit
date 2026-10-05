using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Pins the contract of a binding without a tooltip pipeline: AvaloniaEdit publishes no completion tooltip member, so opening a window logs
/// nothing about a tooltip and publishes no tooltip state.
/// </summary>
[AvaloniaTestClass]
public sealed class CompletionPresenterWithoutTooltipTests
{
	[TestMethod]
	public void OpenOrRefresh_PublishesNoTooltipStateAndLogsNothing()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			using var logger = new RecordingLogger();
			using CompletionSession session = CreateController(editor, logger: logger);

			Assert.IsTrue(session.Completion.OpenOrRefresh([CreateItem("sample"), CreateItem("sampler")], TextRange.FromBounds(0, 6)));
			DispatcherTestHelpers.PumpUntilIdle();

			Assert.IsFalse(session.Completion.IsTooltipUpdatePending);
			Assert.IsFalse(session.Controller.CurrentPresentation.IsTooltipVisible);
			Assert.IsNull(session.Controller.CurrentPresentation.TooltipContent);
			Assert.IsEmpty(logger.Entries);
		}
	}
}
