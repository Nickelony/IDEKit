namespace Nickelony.Samples;

/// <summary>A one-line message about a command that ran, failed or was applied.</summary>
/// <param name="message">The message the status line shows.</param>
internal sealed class StatusChangedEventArgs(string message) : EventArgs
{
	internal string Message { get; } = message;
}
