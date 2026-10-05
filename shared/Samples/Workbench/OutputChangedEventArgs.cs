namespace Nickelony.Samples;

/// <summary>The text that a run of the settings command produced.</summary>
/// <param name="output">The captured output and the way the run ended.</param>
internal sealed class OutputChangedEventArgs(string output) : EventArgs
{
	internal string Output { get; } = output;
}
