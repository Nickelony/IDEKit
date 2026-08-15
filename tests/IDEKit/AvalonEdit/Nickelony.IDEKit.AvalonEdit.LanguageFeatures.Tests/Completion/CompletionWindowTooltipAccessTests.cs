using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class CompletionWindowTooltipAccessTests
{
	[TestMethod]
	public void TryGetTooltip_ReturnsCompletionWindowTooltip()
	{
		var editor = new ICSharpCode.AvalonEdit.TextEditor();
		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			completionWindow.Show();

			bool resolved = CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip);

			Assert.IsTrue(resolved);
			Assert.IsNotNull(tooltip);

			// The reflected AvalonEdit field is what the controller depends on; losing it must fail this
			// test when the AvalonEdit version is upgraded.
			Assert.IsTrue(CompletionWindowTooltipAccess.IsFieldAvailable);

			completionWindow.Close();
		}
	}
}
