using System;
using System.IO;

namespace Nickelony.Repository.Tests;

/// <summary>Locates the repository root that the guard reads.</summary>
internal static class RepositoryPaths
{
	private const string SolutionFile = "Nickelony.IDEKit.slnx";

	/// <summary>Finds the root by walking up for the solution file, and fails when it is absent.</summary>
	internal static string Root()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
				return directory.FullName;

			directory = directory.Parent;
		}

		Assert.Fail(
			$"The repository root could not be found above '{AppContext.BaseDirectory}', so the repository "
				+ "guards cannot run and this build proves nothing about them. A full checkout is needed."
		);

		return string.Empty;
	}

	internal static string Below(params string[] segments) => Path.Combine([Root(), .. segments]);
}
