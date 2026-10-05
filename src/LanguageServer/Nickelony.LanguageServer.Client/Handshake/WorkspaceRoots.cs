using Nickelony.Infrastructure;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>The normalized workspace roots of one client, with the workspace folders advertised for them.</summary>
internal sealed class WorkspaceRoots
{
	private WorkspaceRoots(IReadOnlyList<string> paths, IReadOnlyList<WorkspaceFolder> folders)
	{
		Paths = paths;
		Folders = folders;
		DisplayText = string.Join(", ", paths);
	}

	/// <summary>The normalized root directory paths, in the caller's order.</summary>
	internal IReadOnlyList<string> Paths { get; }

	/// <summary>The workspace folders advertised to the server, one per root in the same order.</summary>
	internal IReadOnlyList<WorkspaceFolder> Folders { get; }

	/// <summary>The roots as one text for log messages.</summary>
	internal string DisplayText { get; }

	/// <summary>Normalizes the host's root directory paths and derives the workspace folders for them.</summary>
	/// <param name="workspaceRootDirectoryPaths">The host's root directories, or an empty list for a folderless session.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains a <see langword="null"/>, empty, whitespace-only, relative, or duplicate entry.
	/// </exception>
	/// <exception cref="NotSupportedException">A root uses a path form the host cannot normalize.</exception>
	/// <exception cref="PathTooLongException">A root exceeds the host path-length limit.</exception>
	internal static WorkspaceRoots Create(IReadOnlyList<string> workspaceRootDirectoryPaths)
	{
		string[] normalizedPaths = LocalPathRules.NormalizeRoots(
			workspaceRootDirectoryPaths,
			LocalPathRules.DefaultComparer,
			nameof(workspaceRootDirectoryPaths)
		);

		var folders = new WorkspaceFolder[normalizedPaths.Length];

		for (int i = 0; i < normalizedPaths.Length; i++)
			folders[i] = new WorkspaceFolder(DocumentUriConversion.CreateFileUri(normalizedPaths[i]), GetFolderName(normalizedPaths[i]));

		return new WorkspaceRoots(Array.AsReadOnly(normalizedPaths), Array.AsReadOnly(folders));
	}

	/// <summary>Derives the workspace-folder display name from the normalized workspace root path.</summary>
	/// <param name="workspaceRootDirectoryPath">The normalized workspace root path.</param>
	/// <returns>The folder name to advertise to the language server.</returns>
	private static string GetFolderName(string workspaceRootDirectoryPath)
	{
		string trimmedWorkspaceRootDirectoryPath = workspaceRootDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (trimmedWorkspaceRootDirectoryPath.Length == 0)
			trimmedWorkspaceRootDirectoryPath = workspaceRootDirectoryPath;

		string folderName = Path.GetFileName(trimmedWorkspaceRootDirectoryPath);

		if (!string.IsNullOrEmpty(folderName))
			return folderName;

		// A filesystem root (for example "C:\" or "/") has no file name; report the root itself rather than its
		// drive or separator, so the advertised name is not the misleading "C:".
		string rootPath = Path.GetPathRoot(workspaceRootDirectoryPath) ?? string.Empty;

		if (!string.IsNullOrEmpty(rootPath))
			return rootPath;

		return workspaceRootDirectoryPath;
	}
}
