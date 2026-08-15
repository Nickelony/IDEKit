#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextCompletionControllerRequestTests
{
	[TestMethod]
	public void ApplyDecision_ItemListEmptiedAfterConstruction_ReturnsFalseWithoutLogging()
	{
		var logger = new CapturingLogger();
		var controller = CreateController(CreateEditor(), logger: logger);

		try
		{
			// A caller-owned decision list can be emptied after construction; the controller must treat an
			// empty item list as a no-op without logging.
			var items = new List<TextCompletionItem> { new("stale") };
			var decision = new TextCompletionSessionDecision(false, items, new TextRange(0, 5));
			items.Clear();

			Assert.IsFalse(controller.ApplyDecision(decision));

			Assert.AreEqual(0, logger.Messages.Count);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void ApplyDecision_NonEmptyItemsWithoutMapper_OpensWindowWithTheDefaultAdapter()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var item = new TextCompletionItem("sample") { Documentation = "Sample documentation." };
				TextCompletionSessionDecision decision = TextCompletionSessionDecision.Open([item], 0, 5);

				// Without a host mapper the decision items go through the package's default adapter, so the
				// standard scenario needs no host glue.
				Assert.IsTrue(hosted.Controller.ApplyDecision(decision));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				var adapters = completionWindow.CompletionList.CompletionData.OfType<TextCompletionItemCompletionData>().ToArray();

				Assert.AreEqual(1, adapters.Length);
				Assert.AreSame(item, adapters[0].Item);
				Assert.AreEqual("sample", adapters[0].Text);
				Assert.AreEqual("Sample documentation.", adapters[0].Description);
				Assert.AreEqual(0.0, adapters[0].Priority);
			}
		}
	}

	[TestMethod]
	public void ApplyDecision_DefaultAdapter_CommitsTheInsertionText()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var item = new TextCompletionItem("sample") { InsertText = "sample_insert" };
				Assert.IsTrue(hosted.Controller.ApplyDecision(TextCompletionSessionDecision.Open([item], 0, 6)));

				var adapters = hosted.Coordinator.ActiveWindow!.CompletionList.CompletionData
					.OfType<TextCompletionItemCompletionData>().ToArray();

				// Committing replaces the completion segment with the explicit insertion text.
				var segment = new TextSegment { StartOffset = 0, Length = 6 };
				adapters[0].Complete(hosted.Controller.WindowCoordinator.ActiveWindow!.TextArea, segment, EventArgs.Empty);
				Assert.AreEqual("sample_insert", hosted.Controller.WindowCoordinator.ActiveWindow!.TextArea.Document.Text);
			}
		}
	}

	[TestMethod]
	public void ApplyDecision_DefaultAdapter_WithoutInsertionText_CommitsTheLabel()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var item = new TextCompletionItem("sample");
				Assert.IsTrue(hosted.Controller.ApplyDecision(TextCompletionSessionDecision.Open([item], 0, 6)));

				var adapters = hosted.Coordinator.ActiveWindow!.CompletionList.CompletionData
					.OfType<TextCompletionItemCompletionData>().ToArray();

				// Without an explicit insertion text the label is the insertion text.
				var segment = new TextSegment { StartOffset = 0, Length = 6 };
				adapters[0].Complete(hosted.Controller.WindowCoordinator.ActiveWindow!.TextArea, segment, EventArgs.Empty);
				Assert.AreEqual("sample", hosted.Controller.WindowCoordinator.ActiveWindow!.TextArea.Document.Text);
			}
		}
	}

	[TestMethod]
	public void ApplyDecision_CloseDecision_ClosesOpenWindowAndReturnsFalse()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample")], 0, 5));
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

				Assert.IsFalse(hosted.Controller.ApplyDecision(TextCompletionSessionDecision.Close));

				// A close decision closes the window without opening a new one.
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsNull(hosted.Coordinator.ActiveWindow);
			}
		}
	}

	[TestMethod]
	public void ApplyDecision_CloseWithItems_ClosesTheOldWindowAndOpensTheNewOne()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample")], 0, 5));

				CompletionWindow oldWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// A decision can both close and open; the close is applied first, so the previous window is
				// closed even though the replacement uses the same replacement start.
				var decision = new TextCompletionSessionDecision(
					shouldClose: true,
					items: [new TextCompletionItem("second")],
					replacementRange: new TextRange(0, 5));

				Assert.IsTrue(hosted.Controller.ApplyDecision(decision));

				CompletionWindow newWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The replacement completion window was not tracked.");

				Assert.AreNotSame(oldWindow, newWindow);
#if AVALONIAEDIT
				Assert.IsFalse(oldWindow.IsOpen);
				Assert.IsTrue(newWindow.IsOpen);
#else
				Assert.IsFalse(oldWindow.IsVisible);
				Assert.IsTrue(newWindow.IsVisible);
#endif
			}
		}
	}

	[TestMethod]
	public void ApplyDecision_ItemFactoryHookReturningNull_ThrowsInvalidOperationException()
	{
		var controller = CompletionTestHost.CreateController(
			CompletionTestHost.CreateEditor(),
			hooks: new TextCompletionControllerHooks
			{
				CompletionItemFactory = _ => null!
			});

		try
		{
			TextCompletionSessionDecision decision = TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5);

			// A null factory result violates the factory contract rather than a parameter, so the failure is
			// reported as an invalid operation that names the offending item.
			var exception = Assert.ThrowsExactly<InvalidOperationException>(() => controller.ApplyDecision(decision));

			StringAssert.Contains(exception.Message, "sample");
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void ApplyDecision_ItemsWithHookMapper_OpensWindowWithMappedItems()
	{
		var mappedItem = new TestCompletionData("sample");
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				CompletionItemFactory = _ => mappedItem
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionSessionDecision decision = TextCompletionSessionDecision.Open(
					[new TextCompletionItem("sample")],
					0,
					5);

				// The hook is the controller's only item-mapping seam, so the mapped instance is what the
				// window displays.
				Assert.IsTrue(hosted.Controller.ApplyDecision(decision));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.AreEqual(1, completionWindow.CompletionList.CompletionData.Count);
				Assert.AreSame(mappedItem, completionWindow.CompletionList.CompletionData[0]);
			}
		}
	}
}
