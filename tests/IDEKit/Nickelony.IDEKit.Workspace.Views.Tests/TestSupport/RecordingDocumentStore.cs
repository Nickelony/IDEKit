using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>A document store that forwards every member to another store and reports the name of each member it serves.</summary>
/// <remarks>
/// <see cref="OnMember"/> runs before the member, on the thread and in the asynchronous flow of the caller, so a test sees where in an operation
/// of the coordinator the store is reached.
/// </remarks>
internal sealed class RecordingDocumentStore(IWorkspaceDocumentStore inner) : IWorkspaceDocumentStore
{
	/// <summary>A callback that receives the name of each member the store serves, before the member runs.</summary>
	public Action<string>? OnMember { get; set; }

	public WorkspaceOptions Options => inner.Options;

	public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath)
	{
		Visit(nameof(GetSnapshotsUnderDirectory));
		return inner.GetSnapshotsUnderDirectory(directoryPath);
	}

	public bool TryGetSnapshot(string? filePath, [NotNullWhen(true)] out WorkspaceDocumentSnapshot? snapshot)
	{
		Visit(nameof(TryGetSnapshot));
		return inner.TryGetSnapshot(filePath, out snapshot);
	}

	public Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default
	)
	{
		Visit(nameof(OpenAsync));
		return inner.OpenAsync(filePath, options, cancellationToken);
	}

	public Task<WorkspaceDocumentCommitResult> CommitAsync(WorkspaceDocumentCommitRequest request, CancellationToken cancellationToken = default)
	{
		Visit(nameof(CommitAsync));
		return inner.CommitAsync(request, cancellationToken);
	}

	public Task<WorkspaceDocumentReloadResult> ReloadAsync(WorkspaceDocumentReloadRequest request, CancellationToken cancellationToken = default)
	{
		Visit(nameof(ReloadAsync));
		return inner.ReloadAsync(request, cancellationToken);
	}

	public Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default
	)
	{
		Visit(nameof(ResolveExternalConflictAsync));
		return inner.ResolveExternalConflictAsync(request, cancellationToken);
	}

	public WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request)
	{
		Visit(nameof(Replace));
		return inner.Replace(request);
	}

	public WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request)
	{
		Visit(nameof(Discard));
		return inner.Discard(request);
	}

	public Task<WorkspaceDocumentRenameResult> RenameAsync(WorkspaceDocumentRenameRequest request, CancellationToken cancellationToken = default)
	{
		Visit(nameof(RenameAsync));
		return inner.RenameAsync(request, cancellationToken);
	}

	public Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(WorkspaceDocumentSaveAsRequest request, CancellationToken cancellationToken = default)
	{
		Visit(nameof(SaveAsAsync));
		return inner.SaveAsAsync(request, cancellationToken);
	}

	public Task<WorkspaceDocumentDeleteResult> DeleteAsync(WorkspaceDocumentDeleteRequest request, CancellationToken cancellationToken = default)
	{
		Visit(nameof(DeleteAsync));
		return inner.DeleteAsync(request, cancellationToken);
	}

	public Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default
	)
	{
		Visit(nameof(RenameDirectoryAsync));
		return inner.RenameDirectoryAsync(request, cancellationToken);
	}

	public Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default
	)
	{
		Visit(nameof(DeleteDirectoryAsync));
		return inner.DeleteDirectoryAsync(request, cancellationToken);
	}

	public ValueTask DisposeAsync() => inner.DisposeAsync();

	private void Visit(string member) => OnMember?.Invoke(member);
}
