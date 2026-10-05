using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nickelony.Repository.Tests;

/// <summary>A project file of the repository, read without building it, and the source files it compiles.</summary>
internal sealed class ProjectFile
{
	private ProjectFile(
		string name,
		string relativePath,
		string relativeDirectory,
		IReadOnlyList<string> projectReferences,
		IReadOnlyList<string> defineConstants,
		string? editorEngine,
		IReadOnlyList<string> sources,
		XDocument document
	)
	{
		Name = name;
		RelativePath = relativePath;
		RelativeDirectory = relativeDirectory;
		ProjectReferences = projectReferences;
		DefineConstants = defineConstants;
		EditorEngine = editorEngine;
		Sources = sources;
		Document = document;
	}

	/// <summary>The project file as XML, for a guard that reads a property or a comment the model does not expose.</summary>
	internal XDocument Document { get; }

	/// <summary>The file name without its extension, which is also the assembly and package name.</summary>
	internal string Name { get; }

	/// <summary>The path of the project file below the repository root, with forward slashes.</summary>
	internal string RelativePath { get; }

	/// <summary>The folder of the project file below the repository root, with forward slashes.</summary>
	internal string RelativeDirectory { get; }

	/// <summary>The names of the projects this project references.</summary>
	internal IReadOnlyList<string> ProjectReferences { get; }

	/// <summary>
	/// The conditional-compilation symbols of the project: the ones its file sets, and <c>AVALONIAEDIT</c> for an Avalonia editor project,
	/// whose engine <c>Directory.Build.targets</c> turns into the symbol.
	/// </summary>
	internal IReadOnlyList<string> DefineConstants { get; }

	/// <summary>
	/// The engine an editor binding project or its test project states with <c>EditorEngine</c> (<c>Wpf</c> or <c>Avalonia</c>),
	/// or <see langword="null"/> for a project that states none.
	/// </summary>
	internal string? EditorEngine { get; }

	/// <summary>The C# files the project compiles, below the repository root, with forward slashes: its own folder and its linked files.</summary>
	internal IReadOnlyList<string> Sources { get; }

	internal bool IsTestProject => RelativePath.StartsWith("tests/", StringComparison.Ordinal);

	/// <summary>Whether the project is a sample application below <c>samples</c>.</summary>
	internal bool IsSampleProject => RelativePath.StartsWith("samples/", StringComparison.Ordinal);

	internal static ProjectFile Load(string absolutePath)
	{
		string root = RepositoryPaths.Root();
		string directory = Path.GetDirectoryName(absolutePath)!;

		var document = XDocument.Load(absolutePath);

		List<string> references =
		[
			.. document
				.Descendants("ProjectReference")
				.Select(item => (string?)item.Attribute("Include"))
				.OfType<string>()
				.Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))),
		];

		string? editorEngine = document.Descendants("EditorEngine").Select(item => item.Value.Trim()).FirstOrDefault(value => value.Length > 0);

		List<string> defines =
		[
			.. document
				.Descendants("DefineConstants")
				.SelectMany(item => item.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				.Where(symbol => !symbol.StartsWith("$(", StringComparison.Ordinal)),
		];

		if (editorEngine == "Avalonia")
			defines.Add("AVALONIAEDIT");

		HashSet<string> sources = new(StringComparer.Ordinal);

		foreach (string file in RepositoryModel.EnumerateFiles(directory, "*.cs"))
			sources.Add(Relative(root, file));

		foreach (XElement item in document.Descendants("Compile"))
		{
			string? include = (string?)item.Attribute("Include");

			if (include is null)
				continue;

			HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);

			foreach (string pattern in SplitPatterns((string?)item.Attribute("Exclude")))
			{
				foreach (string file in ExpandInclude(directory, pattern))
					excluded.Add(file);
			}

			foreach (string file in ExpandInclude(directory, include))
			{
				if (!excluded.Contains(file))
					sources.Add(Relative(root, file));
			}
		}

		foreach (string remove in document.Descendants("Compile").Select(item => (string?)item.Attribute("Remove")).OfType<string>())
		{
			foreach (string file in ExpandInclude(directory, remove))
				sources.Remove(Relative(root, file));
		}

		return new ProjectFile(
			name: Path.GetFileNameWithoutExtension(absolutePath),
			relativePath: Relative(root, absolutePath),
			relativeDirectory: Relative(root, directory),
			projectReferences: references,
			defineConstants: defines,
			editorEngine,
			sources: [.. sources.OrderBy(path => path, StringComparer.Ordinal)],
			document
		);
	}

	private static string[] SplitPatterns(string? patterns) =>
		patterns?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

	private static IEnumerable<string> ExpandInclude(string directory, string include)
	{
		string normalized = include.Replace('\\', '/');

		if (!normalized.Contains('*', StringComparison.Ordinal))
		{
			string full = Path.GetFullPath(Path.Combine(directory, normalized));

			if (File.Exists(full))
				yield return full;

			yield break;
		}

		int wildcard = normalized.IndexOf('*', StringComparison.Ordinal);
		int cut = normalized.LastIndexOf('/', wildcard);

		string baseDirectory = Path.GetFullPath(Path.Combine(directory, cut < 0 ? "." : normalized[..cut]));

		if (!Directory.Exists(baseDirectory))
			yield break;

		string pattern =
			"^"
			+ Regex.Escape(normalized).Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal)
			+ "$";

		Regex matcher = new(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

		foreach (string file in RepositoryModel.EnumerateFiles(baseDirectory, "*"))
		{
			string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');

			if (matcher.IsMatch(relative))
				yield return file;
		}
	}

	private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
