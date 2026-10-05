using Nickelony.IDEKit.LanguageFeatures.Synchronization;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>A synchronization target that records each call as one line, such as <c>open a.lua: text</c>, with the file names of the ids.</summary>
internal sealed class RecordingSynchronizationTarget : IDocumentSynchronizationTarget
{
	public List<string> Calls { get; } = [];

	public void OpenDocument(string documentId, string documentText) => Calls.Add($"open {Path.GetFileName(documentId)}: {documentText}");

	public void UpdateDocument(string documentId, string documentText) => Calls.Add($"update {Path.GetFileName(documentId)}: {documentText}");

	public void RenameDocument(string oldDocumentId, string newDocumentId, string documentText) =>
		Calls.Add($"rename {Path.GetFileName(oldDocumentId)} to {Path.GetFileName(newDocumentId)}: {documentText}");

	public void CloseDocument(string documentId) => Calls.Add($"close {Path.GetFileName(documentId)}");
}
