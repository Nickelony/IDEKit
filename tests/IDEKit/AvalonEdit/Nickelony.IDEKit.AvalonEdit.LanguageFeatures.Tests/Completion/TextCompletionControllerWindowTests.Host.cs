using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the completion window's host-composition and interaction branches: a click on an inline-based
/// item template, the activatable-window option, a bare <see cref="TextArea"/> host without a
/// <see cref="TextEditor"/> wrapper, and a missing or detached document.
/// </summary>
public sealed partial class TextCompletionControllerWindowTests
{
	[TestMethod]
	public void CompletionListClick_InlineBasedItemTemplate_SelectsTheClickedItem()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				(ListBox listBox, Run run) = PrepareInlineTemplateClick(completionWindow);

				var eventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
				{
					RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
					Source = run
				};

				listBox.RaiseEvent(eventArgs);

				// The click selected the clicked item instead of throwing from the non-visual original source.
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

				(ListBox listBox, Run run) = PrepareInlineTemplateClick(completionWindow);
				object? selectionBeforeClick = listBox.SelectedItem;

				var eventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
				{
					RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
					Source = run
				};

				listBox.RaiseEvent(eventArgs);

				// With an activatable window the controller installs neither the activation hook nor the
				// explicit click selection; a synthetic click cannot change the selection, so it stays as it
				// was. (The non-activating default selects the clicked item; its click path is pinned by
				// CompletionListClick_InlineBasedItemTemplate_SelectsTheClickedItem.)
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

		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(textArea);

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
			Document = null
		};

		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(textArea);

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
				// passed to AvalonEdit's filtering and rendering.
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
				hosted.Editor.TextArea.Document = null;

				DispatcherTestUtils.PumpUntilIdle(DispatcherPriority.ApplicationIdle);

				// Reattaching a document restores the normal path before teardown.
				hosted.Editor.TextArea.Document = new TextDocument("sample");

				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 3));
			}
		}
	}

	// Applies the shared inline-based item template used by the click tests and waits for the second item
	// container, returning the list and the clicked Run.
	private static (ListBox ListBox, Run Run) PrepareInlineTemplateClick(CompletionWindow completionWindow)
	{
		ListBox listBox = completionWindow.CompletionList.ListBox;

		// An inline-based item template makes the click's original source a Run, a non-visual ContentElement;
		// resolving the item container must not fall back to a visual-tree walk.
		listBox.ItemTemplate = (DataTemplate)XamlReader.Parse(
			"<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
			+ "<TextBlock><Run Text=\"{Binding Text, Mode=OneWay}\" /></TextBlock></DataTemplate>");

		completionWindow.UpdateLayout();

		// Container realization can need more than one layout pass on a loaded machine, so the helper waits
		// for the container instead of trusting a fixed delay.
		DispatcherTestUtils.PumpUntil(() => listBox.ItemContainerGenerator.ContainerFromIndex(1) is not null);

		completionWindow.UpdateLayout();

		var container = listBox.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem
			?? throw new InvalidOperationException("The second item container was not realized.");

		TextBlock textBlock = WPFTestHost.FindVisualDescendants<TextBlock>(container).FirstOrDefault()
			?? throw new InvalidOperationException("The item template's TextBlock was not found.");

		return (listBox, textBlock.Inlines.OfType<Run>().First());
	}
}
