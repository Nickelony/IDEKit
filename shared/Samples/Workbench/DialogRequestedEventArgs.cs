namespace Nickelony.Samples;

/// <summary>Carries the child window that a command asks the window to open.</summary>
/// <param name="dialog">The child window to open.</param>
/// <param name="path">
/// The file or folder the window acts on, such as the folder of a new file, or <see langword="null"/> when it acts on none.
/// </param>
internal sealed class DialogRequestedEventArgs(SampleDialogKind dialog, string? path = null) : EventArgs
{
	internal SampleDialogKind Dialog { get; } = dialog;
	internal string? Path { get; } = path;
}
