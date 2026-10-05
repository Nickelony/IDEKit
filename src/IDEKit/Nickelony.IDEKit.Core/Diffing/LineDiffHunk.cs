namespace Nickelony.IDEKit.Core.Diffing;

/// <summary>Describes one run of changed lines: the baseline lines it replaces and the current lines that replace them.</summary>
/// <param name="BaselineStart">The zero-based index of the first replaced baseline line, or of the baseline line an insertion precedes.</param>
/// <param name="BaselineCount">The number of replaced baseline lines; zero for an insertion.</param>
/// <param name="CurrentStart">The zero-based index of the first current line of the run.</param>
/// <param name="CurrentCount">The number of current lines; zero for a deletion.</param>
public readonly record struct LineDiffHunk(int BaselineStart, int BaselineCount, int CurrentStart, int CurrentCount);
