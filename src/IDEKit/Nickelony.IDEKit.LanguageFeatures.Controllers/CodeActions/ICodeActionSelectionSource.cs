using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

/// <summary>Reports the editor context a <see cref="CodeActionController"/> builds its requests from, and when that context changes.</summary>
/// <remarks>
/// <para>
/// A binding implements the source over its text area, and a host with another editor implements it over its own state. The controller disposes
/// the source when the controller is disposed, which can happen on any thread.
/// </para>
/// <para>
/// The controller answers a notification by reading the source and updating its published state,
/// so a source raises <see cref="IChangeNotificationSource.Changed"/> and <see cref="DocumentReplaced"/> on the thread of the controller's dispatcher
/// and accepts reads there only. A notification raised on another thread makes the controller throw <see cref="InvalidOperationException"/>
/// into the raising code.
/// </para>
/// <para>
/// <see cref="IChangeNotificationSource.Changed"/> reports a caret, selection, or document content change, which the controller
/// answers by scheduling a refresh. <see cref="DocumentReplaced"/> reports that the editor shows another document, after the source
/// already attached to it; the controller then clears its published state before it schedules the refresh.
/// </para>
/// </remarks>
public interface ICodeActionSelectionSource : IChangeNotificationSource, IDisposable
{
	/// <summary>Raised after the editor started showing another document.</summary>
	event EventHandler? DocumentReplaced;

	/// <summary>The one-based line the caret is on, or <see langword="null"/> when the editor has no document.</summary>
	int? CaretLineNumber { get; }

	/// <summary>Captures the context a request is built from: the document text, the caret, and the selection.</summary>
	/// <returns>The context, or <see langword="null"/> when the editor has no document.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The editor reports a caret or selection that its document text does not contain, which <see cref="CodeActionSelection"/> rejects.
	/// </exception>
	/// <remarks>
	/// The controller logs a failure of this member, and it clears its published state unless a newer context change already superseded the attempt.
	/// </remarks>
	CodeActionSelection? Capture();
}
