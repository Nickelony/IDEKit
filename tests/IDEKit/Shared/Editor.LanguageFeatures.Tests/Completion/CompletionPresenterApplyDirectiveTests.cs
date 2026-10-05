#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionPresenterApplyDirectiveTests
{
	[TestMethod]
	public void ApplyDirective_ItemListEmptiedAfterConstruction_ReturnsFalseWithoutLogging()
	{
		var logger = new RecordingLogger();
		CompletionSession session = CreateController(CreateEditor(), logger: logger);

		try
		{
			// A caller-owned decision list can be emptied after construction; the controller must treat an
			// empty item list as a no-op without logging.
			var items = new List<CompletionItem> { new("stale") };
			var decision = new CompletionSessionDirective(false, items, new TextRange(0, 5));

			items.Clear();

			Assert.IsFalse(session.Completion.ApplyDirective(decision));

			Assert.IsEmpty(logger.Messages);
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void ApplyDirective_NonEmptyItemsWithoutMapper_OpensWindowWithTheDefaultAdapter()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var item = new CompletionItem("sample") { Documentation = "Sample documentation." };
					var decision = CompletionSessionDirective.Open([item], TextRange.FromBounds(0, 5));

					// Without a host mapper the decision items go through the package's default adapter, so the
					// standard scenario needs no host glue.
					Assert.IsTrue(hosted.Completion.ApplyDirective(decision));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					CompletionItemAdapter[] adapters = [.. completionWindow.CompletionList.CompletionData.OfType<CompletionItemAdapter>()];

					Assert.HasCount(1, adapters);
					Assert.AreSame(item, adapters[0].Item);
					Assert.AreEqual("sample", adapters[0].Text);
					Assert.AreEqual("Sample documentation.", adapters[0].Description);
					Assert.AreEqual(0.0, adapters[0].Priority);
				}
			}
		}
	}

	[TestMethod]
	public void ApplyDirective_DefaultAdapter_CommitsTheInsertionText()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var item = new CompletionItem("sample") { InsertText = "sample_insert" };
					Assert.IsTrue(hosted.Completion.ApplyDirective(CompletionSessionDirective.Open([item], TextRange.FromBounds(0, 6))));

					CompletionItemAdapter[] adapters =
					[
						.. hosted.Presenter.ActiveWindow!.CompletionList.CompletionData.OfType<CompletionItemAdapter>(),
					];

					// Committing replaces the completion segment with the explicit insertion text.
					var segment = new TextSegment { StartOffset = 0, Length = 6 };
					adapters[0].Complete(hosted.Completion.WindowPresenter.ActiveWindow!.TextArea, segment, EventArgs.Empty);
					Assert.AreEqual("sample_insert", hosted.Completion.WindowPresenter.ActiveWindow!.TextArea.Document.Text);
				}
			}
		}
	}

	[TestMethod]
	public void ApplyDirective_ItemAdapterHooks_ShowTheHookImageAndAdjustTheCommit()
	{
		var image = new DrawingImage();

		var hooks = new CompletionPresenterHooks
		{
			ItemAdapter = new CompletionItemAdapterHooks
			{
				ImageFor = _ => image,
				AdjustInsertion = static (_, _, insertion) => insertion with { Text = insertion.Text + "()" },
			},
		};

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks, text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var item = new CompletionItem("sample") { InsertText = "sample_insert" };
					Assert.IsTrue(hosted.Completion.ApplyDirective(CompletionSessionDirective.Open([item], TextRange.FromBounds(0, 6))));

					CompletionWindow window =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					CompletionItemAdapter[] adapters = [.. window.CompletionList.CompletionData.OfType<CompletionItemAdapter>()];

					// The controller hands the hooks to each default adapter, so the window shows the hook's image and the
					// commit goes through the hook's adjustment.
					Assert.HasCount(1, adapters);
					Assert.AreSame(image, adapters[0].Image);

					adapters[0].Complete(window.TextArea, new TextSegment { StartOffset = 0, Length = 6 }, EventArgs.Empty);
					Assert.AreEqual("sample_insert()", window.TextArea.Document.Text);
				}
			}
		}
	}

	[TestMethod]
	public void ApplyDirective_DefaultAdapter_WithoutInsertionText_CommitsTheLabel()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var item = new CompletionItem("sample");
					Assert.IsTrue(hosted.Completion.ApplyDirective(CompletionSessionDirective.Open([item], TextRange.FromBounds(0, 6))));

					CompletionItemAdapter[] adapters =
					[
						.. hosted.Presenter.ActiveWindow!.CompletionList.CompletionData.OfType<CompletionItemAdapter>(),
					];

					// Without an explicit insertion text the label is the insertion text.
					var segment = new TextSegment { StartOffset = 0, Length = 6 };
					adapters[0].Complete(hosted.Completion.WindowPresenter.ActiveWindow!.TextArea, segment, EventArgs.Empty);
					Assert.AreEqual("sample", hosted.Completion.WindowPresenter.ActiveWindow!.TextArea.Document.Text);
				}
			}
		}
	}

	[TestMethod]
	public void ApplyDirective_CloseDecision_ClosesOpenWindowAndReturnsFalse()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new StubCompletionData("sample")], TextRange.FromBounds(0, 5)));
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

					Assert.IsFalse(hosted.Completion.ApplyDirective(CompletionSessionDirective.Close));

					// A close decision closes the window without opening a new one.
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.IsNull(hosted.Presenter.ActiveWindow);
				}
			}
		}
	}

	[TestMethod]
	public void ApplyDirective_CloseWithItems_ClosesTheOldWindowAndOpensTheNewOne()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Completion.OpenOrRefresh([new StubCompletionData("sample")], TextRange.FromBounds(0, 5)));

				CompletionWindow oldWindow =
					hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

				// A decision can both close and open; the close is applied first, so the previous window is
				// closed even though the replacement uses the same replacement start.
				var decision = new CompletionSessionDirective(
					shouldClose: true,
					items: [new CompletionItem("second")],
					replacementRange: new TextRange(0, 5)
				);

				Assert.IsTrue(hosted.Completion.ApplyDirective(decision));

				CompletionWindow newWindow =
					hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The replacement completion window was not tracked.");

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
	public void ApplyDirective_ItemFactoryHookReturningNull_ThrowsInvalidOperationException()
	{
		CompletionSession session = CompletionTestHelpers.CreateController(
			CompletionTestHelpers.CreateEditor(),
			hooks: new CompletionPresenterHooks { CompletionItemFactory = _ => null! }
		);

		try
		{
			var decision = CompletionSessionDirective.Open([new CompletionItem("sample")], TextRange.FromBounds(0, 5));

			// A null factory result violates the factory contract rather than a parameter, so the failure is
			// reported as an invalid operation that names the offending item.
			InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() => session.Completion.ApplyDirective(decision));

			Assert.Contains("sample", exception.Message);
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void ApplyDirective_ItemsWithHookMapper_OpensWindowWithMappedItems()
	{
		var mappedItem = new StubCompletionData("sample");

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: new CompletionPresenterHooks { CompletionItemFactory = _ => mappedItem });

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var decision = CompletionSessionDirective.Open([new CompletionItem("sample")], TextRange.FromBounds(0, 5));

					// The hook is the controller's only item-mapping hook, so the mapped instance is what the
					// window displays.
					Assert.IsTrue(hosted.Completion.ApplyDirective(decision));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.HasCount(1, completionWindow.CompletionList.CompletionData);
					Assert.AreSame(mappedItem, completionWindow.CompletionList.CompletionData[0]);
				}
			}
		}
	}
}
