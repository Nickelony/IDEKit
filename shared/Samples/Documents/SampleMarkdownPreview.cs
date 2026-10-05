using Nickelony.IDEKit.Workspace;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Nickelony.IDEKit.AvaloniaEdit.Media;
#else
using System.Windows;
using System.Windows.Controls;
using Nickelony.IDEKit.AvalonEdit.Media;
#endif

namespace Nickelony.Samples;

/// <summary>Shows a Markdown document rendered, as a second view of the document beside its editor.</summary>
/// <remarks>
/// The preview is read-only, so it never has pending edits and never publishes. The coordinator hands it every new snapshot of the document,
/// whether an editor published it, a reload read it or a language-server edit made it, and the preview renders it again.
/// </remarks>
internal sealed class SampleMarkdownPreview : IWorkspaceDocumentView
{
	private string _markdown = string.Empty;
	private SampleTheme _theme;

	/// <param name="viewId">The stable identifier of the view, which the coordinator reports in its results.</param>
	/// <param name="theme">The look of the rendered document.</param>
	internal SampleMarkdownPreview(string viewId, SampleTheme theme)
	{
		ViewId = viewId;
		_theme = theme;
	}

	public event EventHandler<ApplyRequestedEventArgs>? ApplyRequested
	{
		add { }
		remove { }
	}

	/// <summary>Raised after a rename gave the document another path, so the tab follows it.</summary>
	internal event EventHandler? DocumentRenamed;

	public string ViewId { get; }

	public WorkspaceDocumentKey? DocumentKey { get; private set; }

	public bool HasPendingEdits => false;

	public bool HasConflict => false;

	/// <summary>The path of the document the preview shows, or <see langword="null"/> before it opens.</summary>
	internal string? DocumentId { get; private set; }

	/// <summary>The control the window shows in the tab of the preview.</summary>
	internal Border Content { get; } = new() { Padding = new Thickness(left: 12, top: 8, right: 12, bottom: 8) };

	public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
	{
		Show(snapshot);
		return new WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenOutcome.Opened);
	}

	public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
	{
		Show(snapshot);
		return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome.Refreshed);
	}

	public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change)
	{
		Show(change.Snapshot);
		DocumentRenamed?.Invoke(this, EventArgs.Empty);

		return new WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityOutcome.Updated);
	}

	public WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
	{
		if (result.Snapshot is { } snapshot)
			Show(snapshot);

		return new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Applied);
	}

	public void Close()
	{
		DocumentKey = null;
		Content.Child = null;
	}

	/// <summary>Renders the document again in the colors of the theme.</summary>
	internal void ApplyTheme(SampleTheme theme)
	{
		_theme = theme;
		Render();
	}

	private void Show(WorkspaceDocumentSnapshot snapshot)
	{
		DocumentKey = snapshot.DocumentKey;
		DocumentId = snapshot.DocumentId;

		// A snapshot whose text did not change, such as the acknowledgment of a save, keeps the rendering and its scroll position.
		string markdown = snapshot.Content;

		if (markdown == _markdown && Content.Child is not null)
			return;

		_markdown = markdown;
		Render();
	}

	// The rendered content is transparent, so the preview paints the editor background behind it.
	private void Render()
	{
		Content.Background = _theme.Background.ToBrush();
		Content.Child = SampleMarkdown.RenderDocument(_markdown, _theme);
	}
}
