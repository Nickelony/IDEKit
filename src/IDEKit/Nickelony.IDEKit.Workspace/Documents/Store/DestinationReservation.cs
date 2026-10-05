namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Represents a destination reserved by an in-progress file or directory move or save-as operation.</summary>
internal sealed class DestinationReservation(string destinationId)
{
	internal string DestinationId { get; } = destinationId;
	internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
