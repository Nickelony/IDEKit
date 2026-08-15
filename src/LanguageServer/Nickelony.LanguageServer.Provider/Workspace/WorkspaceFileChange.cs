namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Represents a workspace file change ready for forwarding.
/// </summary>
/// <param name="Path">The affected file path.</param>
/// <param name="Kind">The effective file change kind.</param>
public readonly record struct WorkspaceFileChange(string Path, FileChangeKind Kind);
