#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionControllerFactoryTests
{
	[TestMethod]
	public void Dismiss_CancelsScheduledRequest()
	{
		var provider = new ScriptedCompletionProvider();

		CompletionSession session = CreateController(CreateEditor(), provider: provider);
		CompletionController controller = session.Controller;

		try
		{
			controller.ScheduleRequest();
			Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

			// A window close cancels pending completion work, including a request that has not fired yet.
			session.Completion.Dismiss();

			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

			DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(50.0));

			Assert.IsEmpty(provider.Requests);
		}
		finally
		{
			session.Dispose();
		}
	}
}
