using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>The files of the workspace roots at one moment: the items in list order, and where each one stands by its path.</summary>
/// <param name="Items">One item per file, ordered by folder and then by file name.</param>
/// <param name="IndexByPath">
/// The position in <paramref name="Items"/> of each file, keyed by its full path under the path comparison of the tracker.
/// </param>
/// <remarks>
/// The value is never mutated after it is built, so any thread can read it, and a tracker hands out the same value until a file is added or removed.
/// </remarks>
internal sealed record WorkspaceFileSnapshot(QuickPickItem[] Items, IReadOnlyDictionary<string, int> IndexByPath);
