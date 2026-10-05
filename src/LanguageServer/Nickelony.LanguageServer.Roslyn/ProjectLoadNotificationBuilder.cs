using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>Builds the notifications that ask the Roslyn language server to load the solutions and projects under the workspace roots.</summary>
/// <remarks>
/// <para>
/// A root that holds exactly one <c>.sln</c> or <c>.slnx</c> file at its top level contributes
/// a <c>solution/open</c> notification for it. The language's project files under the other roots go into one <c>project/open</c> notification.
/// The walk skips the <c>bin</c>, <c>obj</c> and <c>node_modules</c> folders, hidden folders and folders whose name starts with a dot, and it visits
/// the shallower folders first.
/// </para>
/// <para>
/// <see cref="RoslynLanguageServerOptions.AutoLoadProjectsMaximum"/> bounds the walk: <c>0</c> sends nothing, a positive value caps the number
/// of project files across the roots, and <see langword="null"/> sends every project file found. A solution is opened whole.
/// </para>
/// </remarks>
internal static class ProjectLoadNotificationBuilder
{
	internal const string SolutionOpenMethod = "solution/open";

	internal const string ProjectOpenMethod = "project/open";

	private static readonly string[] s_solutionFileExtensions = [".sln", ".slnx"];

	private static readonly string[] s_skippedFolderNames = ["bin", "obj", "node_modules"];

	private static readonly EnumerationOptions s_enumerationOptions = new()
	{
		AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
		IgnoreInaccessible = true,
		RecurseSubdirectories = false,
	};

	/// <summary>Builds the notifications for one session start.</summary>
	/// <param name="descriptor">The language whose project files are opened.</param>
	/// <param name="options">The session options that bound the walk.</param>
	/// <param name="workspaceRootDirectoryPaths">The normalized workspace roots, in caller order.</param>
	/// <returns>
	/// The <c>solution/open</c> notifications in root order, then the <c>project/open</c> notification when a project file was found.
	/// </returns>
	internal static IReadOnlyList<SessionStartNotification> Build(
		RoslynLanguageDescriptor descriptor,
		RoslynLanguageServerOptions options,
		IReadOnlyList<string> workspaceRootDirectoryPaths
	)
	{
		int? maximum = options.AutoLoadProjectsMaximum;

		if (maximum is 0)
			return [];

		var notifications = new List<SessionStartNotification>();
		var projectFilePaths = new List<string>();
		var seenProjectFilePaths = new HashSet<string>(StringComparer.Ordinal);

		for (int i = 0; i < workspaceRootDirectoryPaths.Count; i++)
		{
			string root = workspaceRootDirectoryPaths[i];

			if (!Directory.Exists(root))
				continue;

			if (FindSingleSolution(root) is { } solutionFilePath)
			{
				notifications.Add(
					new SessionStartNotification(
						SolutionOpenMethod,
						new JsonObject { ["solution"] = DocumentUriConversion.CreateFileUri(solutionFilePath) }
					)
				);

				continue;
			}

			CollectProjectFiles(root, projectFileExtension: descriptor.ProjectFileExtension, maximum, projectFilePaths, seenProjectFilePaths);
		}

		if (projectFilePaths.Count > 0)
		{
			var projects = new JsonArray();

			for (int i = 0; i < projectFilePaths.Count; i++)
				projects.Add(DocumentUriConversion.CreateFileUri(projectFilePaths[i]));

			notifications.Add(new SessionStartNotification(ProjectOpenMethod, new JsonObject { ["projects"] = projects }));
		}

		return notifications;
	}

	/// <summary>Finds the one solution file at the top level of a root.</summary>
	/// <param name="root">The root directory.</param>
	/// <returns>The solution file path, or <see langword="null"/> when the root holds none or more than one.</returns>
	private static string? FindSingleSolution(string root)
	{
		string? found = null;

		foreach (string filePath in Directory.EnumerateFiles(root, "*", s_enumerationOptions))
		{
			if (!IsSolutionFile(filePath))
				continue;

			if (found is not null)
				return null;

			found = filePath;
		}

		return found;
	}

	/// <summary>Collects the project files under a root, shallower folders first, until the cap is reached.</summary>
	/// <param name="root">The root directory.</param>
	/// <param name="projectFileExtension">The project file extension of the language, for example <c>.csproj</c>.</param>
	/// <param name="maximum">The cap on the collected project files across every root, or <see langword="null"/> for none.</param>
	/// <param name="projectFilePaths">The collected project file paths, in walk order.</param>
	/// <param name="seenProjectFilePaths">The collected paths, so a project under two nested roots is opened once.</param>
	private static void CollectProjectFiles(
		string root,
		string projectFileExtension,
		int? maximum,
		List<string> projectFilePaths,
		HashSet<string> seenProjectFilePaths
	)
	{
		var pending = new Queue<string>();
		pending.Enqueue(root);

		while (pending.Count > 0 && !(projectFilePaths.Count >= maximum))
		{
			string directory = pending.Dequeue();

			string[] files = [.. Directory.EnumerateFiles(directory, "*", s_enumerationOptions)];
			Array.Sort(files, StringComparer.Ordinal);

			foreach (string filePath in files)
			{
				if (projectFilePaths.Count >= maximum)
					return;

				if (
					string.Equals(Path.GetExtension(filePath), projectFileExtension, StringComparison.OrdinalIgnoreCase)
					&& seenProjectFilePaths.Add(filePath)
				)
				{
					projectFilePaths.Add(filePath);
				}
			}

			string[] subdirectories = [.. Directory.EnumerateDirectories(directory, "*", s_enumerationOptions)];
			Array.Sort(subdirectories, StringComparer.Ordinal);

			foreach (string subdirectory in subdirectories)
			{
				if (!IsSkippedFolder(Path.GetFileName(subdirectory)))
					pending.Enqueue(subdirectory);
			}
		}
	}

	private static bool IsSolutionFile(string filePath)
	{
		string extension = Path.GetExtension(filePath);

		for (int i = 0; i < s_solutionFileExtensions.Length; i++)
		{
			if (string.Equals(extension, s_solutionFileExtensions[i], StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	private static bool IsSkippedFolder(string folderName)
	{
		if (folderName.StartsWith('.'))
			return true;

		for (int i = 0; i < s_skippedFolderNames.Length; i++)
		{
			if (string.Equals(folderName, s_skippedFolderNames[i], StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}
}
