using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the completion window's host-composition and interaction branches: a click on an item container,
/// the activatable-window option, a bare <see cref="TextArea"/> host without a <see cref="TextEditor"/>
/// wrapper, and a missing or detached document.
/// </summary>
public sealed partial class TextCompletionControllerWindowTests
{
	[TestMethod]
	public void CompletionListClick_OnAnItemContainer_SelectsTheClickedItem()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Avalonia divergence: the WPF reference templated the items with an inline-based template
				// whose click source was a non-visual Run and raised the click on the list box with that Run
				// as the source. An Avalonia routed event always resets Source to the element it is raised on
				// and bubbles through the visual tree only, so the mirror raises the left-button release on
				// the realized item container; the controller's ancestor walk from a visual source selects
				// the clicked item through the same explicit click-selection path.
				ListBox listBox = completionWindow.CompletionList.ListBox;
				PointerReleasedEventArgs eventArgs = RaiseItemContainerPointerReleased(completionWindow, 1);

				// The click selected the clicked item instead of throwing from the source resolution.
				Assert.AreEqual("second", (listBox.SelectedItem as ICompletionData)?.Text);
				Assert.IsFalse(eventArgs.Handled);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ActivatableWindowOption_DoesNotInstallTheExplicitClickSelection()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(options: TextCompletionControllerOptions.Default with
			{
				NonActivatingWindow = false
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				ListBox listBox = completionWindow.CompletionList.ListBox;
				object? selectionBeforeClick = listBox.SelectedItem;

				RaiseItemContainerPointerReleased(completionWindow, 1);

				// With an activatable window the controller installs neither the activation hook nor the
				// explicit click selection; a synthetic left-button release cannot change the selection
				// (AvaloniaEdit's selection runs on the pointer-press stage), so it stays as it was. (The
				// non-activating default selects the clicked item; its click path is pinned by
				// CompletionListClick_OnAnItemContainer_SelectsTheClickedItem.)
				Assert.AreSame(selectionBeforeClick, listBox.SelectedItem);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_BareTextAreaHost_OpensWindow()
	{
		// A TextArea-only host - no TextEditor wrapper - can drive the completion window directly: the
		// controller reads only text-area state (the document, the dispatcher, and the typography), so a
		// custom control that composes its own TextArea is a supported composition point.
		var textArea = new TextArea
		{
			Document = new TextDocument("sample")
		};

		HostWindow hostWindow = AvaloniaTestHost.ShowInHostWindow(textArea);

		using (hostWindow)
		{
			using var controller = new TextCompletionController(textArea, CreateSkin(), FastOptions);

			Assert.IsTrue(controller.OpenOrRefresh([CreateItem("sample")], 0, 5));
			Assert.IsNotNull(controller.WindowCoordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_WithoutDocument_ReportsNoOpAndDoesNotThrow()
	{
		// A bare text area can exist without a document; the controller must treat the call as a no-op
		// instead of dereferencing the missing document.
		var textArea = new TextArea
		{
			Document = null!
		};

		HostWindow hostWindow = AvaloniaTestHost.ShowInHostWindow(textArea);

		using (hostWindow)
		{
			using var controller = new TextCompletionController(textArea, CreateSkin(), FastOptions);

			Assert.IsNull(textArea.Document);
			Assert.IsFalse(controller.OpenOrRefresh([CreateItem("sample")], 0, 3));
			Assert.IsFalse(controller.WindowCoordinator.IsWindowOpen);

			// Attaching a document restores the normal open path.
			textArea.Document = new TextDocument("sample");

			Assert.IsTrue(controller.OpenOrRefresh([CreateItem("sample")], 0, 3));
			Assert.IsNotNull(controller.WindowCoordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_NullEntries_AreDropped()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedEditorController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// A misbehaving host item factory can surface a null entry; it is dropped instead of being
				// passed to AvaloniaEdit's filtering and rendering.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), null!, CreateItem("second")], 0, 3));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.AreEqual(2, completionWindow.CompletionList.CompletionData.Count);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_DocumentDetachedBeforeInitialSelection_DoesNotThrow()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedEditorController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 3));

				// The posted initial selection must survive a document that detaches before it runs: the
				// window query falls back to the empty query instead of dereferencing the missing document.
				hosted.Editor.TextArea.Document = null!;

				DispatcherTestUtils.PumpUntilIdle(DispatcherPriority.ApplicationIdle);

				// Reattaching a document restores the normal path before teardown.
				hosted.Editor.TextArea.Document = new TextDocument("sample");

				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 3));
			}
		}
	}

	// Raises a left-button pointer release on the realized item container at the given index, exercising the
	// controller's explicit click-selection handler through its public routed-event entry point.
	private static PointerReleasedEventArgs RaiseItemContainerPointerReleased(CompletionWindow completionWindow, int index)
	{
		ListBox listBox = completionWindow.CompletionList.ListBox;

		completionWindow.CompletionList.UpdateLayout();

		// Container realization can need more than one layout pass on a loaded machine, so the helper waits
		// for the container instead of trusting a fixed delay.
		DispatcherTestUtils.PumpUntil(() => listBox.ContainerFromIndex(index) is not null);

		var container = listBox.ContainerFromIndex(index) as ListBoxItem
			?? throw new InvalidOperationException("The item container was not realized.");

		var eventArgs = new PointerReleasedEventArgs(
			source: container,
			pointer: new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
			rootVisual: container,
			rootVisualPosition: default,
			timestamp: 0UL,
			properties: default,
			modifiers: KeyModifiers.None,
			initialPressMouseButton: MouseButton.Left);

		container.RaiseEvent(eventArgs);

		return eventArgs;
	}
}
