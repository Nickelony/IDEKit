using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A test project keeps its helpers apart from its tests, so a reader finds a helper in a <c>TestSupport</c> folder, a helper that several projects
/// compile is written once, and a type that is no test class takes a name from the test vocabulary of <c>CONVENTIONS.md</c>.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class TestOrganizationGuardTests
{
	private const string Guard = nameof(TestOrganizationGuardTests);

	private const string SupportFolder = "TestSupport";

	// The prefix of a path that belongs to the guard project, whose model types name what they model and are no test doubles.
	private static readonly string s_guardProjectName = typeof(TestOrganizationGuardTests).Assembly.GetName().Name!;

	// A file of a test project that declares no test class and sits outside a TestSupport folder, keyed by the path.
	private static readonly AllowList s_misplacedFiles = new(
		"misplaced test files",

		(
			"tests/LanguageServer/Nickelony.LanguageServer.Client.Tests.FakeServer/Program.cs",
			"The entry point of the fake language server, an executable and no test assembly."
		)
	);

	// A top-level type of a test project that is no test class and breaks the test vocabulary, keyed by the path of its file, so a second
	// copy of a name in another file fails the guard.
	private static readonly AllowList s_vocabularyTypes = new(
		"test vocabulary",

		(
			"tests/IDEKit/TestSupport/AvaloniaTestApp.cs",
			"The Avalonia application subclass that the editor suites start, named for the toolkit type it extends; it adds the renderer and the editor theme to the plain one."
		),

		(
			"tests/TestSupport/Avalonia/AvaloniaTestApp.cs",
			"The plain Avalonia application subclass that the headless session starts, named for the toolkit type it extends."
		),

		(
			"tests/TestSupport/Avalonia/HostWindow.cs",
			"The Avalonia window that hosts the control under test, named for the toolkit window it wraps; the WPF twin shares the name."
		),

		(
			"tests/TestSupport/Wpf/HostWindow.cs",
			"The WPF window that hosts the control under test, named for the toolkit window it wraps; the Avalonia twin shares the name."
		),

		(
			"tests/IDEKit/TestSupport/PreparedBatchFactory.cs",
			"Creates the prepared edit batches that the editing tests apply, through the kernel path a host uses."
		),

		("tests/TestSupport/TemporaryDirectory.cs", "A disposable scratch directory, named for the resource it owns."),
		("tests/KeyBindings/TestSupport/TestCommand.cs", "The command-id enum that the key-binding suites use as their command type."),

		(
			"tests/IDEKit/Nickelony.IDEKit.Workspace.Tests/TestSupport/TestSignal.cs",
			"The one-shot wait that the file-system doubles of the store tests signal."
		)
	);

	// A top-level folder of a test project that names no source folder of the project it tests, keyed "{project}/{folder}".
	private static readonly AllowList s_testFolders = new(
		"test folders",
		("Nickelony.LanguageServer.Lua.Tests/Integration", "The tests drive a real language-server process and name no source folder."),
		("Nickelony.LanguageServer.Roslyn.Tests/Integration", "The tests drive a real language-server process and name no source folder.")
	);

	private static readonly Lazy<IReadOnlyList<TestType>> s_types = new(ReadTypes);

	[TestMethod]
	public void NoTestTypeEndsInARetiredWord()
	{
		List<string> violations = [];

		foreach (string path in RepositoryModel.SourcePathsUnder("tests"))
		{
			foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch([]))
			{
				foreach (BaseTypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
				{
					string name = type.Identifier.ValueText;
					string last = VocabularyTable.LastWord(name);

					if (
						VocabularyTable.RetiredWords.Contains(last, StringComparer.Ordinal) && !name.EndsWith("TestHelpers", StringComparison.Ordinal)
					)
					{
						violations.Add(
							$"{path}: {name} ends in the retired word {last}. Name it for what it does, with a last word from the vocabulary table of CONVENTIONS.md."
						);
					}
				}
			}
		}

		Assert.IsEmpty(
			violations,
			"Retired role words in tests:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void AFileWithoutATestClassSitsInATestSupportFolder()
	{
		List<string> violations = [];

		foreach (IGrouping<string, TestType> file in s_types.Value.GroupBy(type => type.Path, StringComparer.Ordinal))
		{
			if (IsInSupportFolder(file.Key) || file.Any(type => type.IsTestClass || type.Name.EndsWith("TestsBase", StringComparison.Ordinal)))
				continue;

			if (!s_misplacedFiles.Allows(file.Key))
				violations.Add(s_misplacedFiles.Rejection($"{file.Key} declares no test class and sits outside a TestSupport folder.", Guard));
		}

		foreach (string path in RepositoryModel.SourcePathsUnder("tests").Where(path => !s_types.Value.Any(type => type.Path == path)))
		{
			string name = Path.GetFileName(path);

			if (name is not ("GlobalUsings.cs" or "AssemblyInfo.cs") && !IsInSupportFolder(path) && !s_misplacedFiles.Allows(path))
				violations.Add(s_misplacedFiles.Rejection($"{path} declares no type and sits outside a TestSupport folder.", Guard));
		}

		violations.AddRange(s_misplacedFiles.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Test files:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
	}

	[TestMethod]
	public void AFileOfASharedSupportFolderIsCompiledByTwoProjects()
	{
		List<string> violations = [];

		IEnumerable<string> shared = RepositoryModel.SourcePathsUnder("tests").Where(path => IsSharedSupportFolder(path.Split('/')));

		foreach (string path in shared)
		{
			int compiling = RepositoryModel.Projects.Count(project =>
				project.IsTestProject && project.Sources.Contains(path, StringComparer.Ordinal)
			);

			if (compiling < 2)
				violations.Add($"{path} is compiled by {compiling} test projects, and a shared support folder holds code that two compile.");
		}

		Assert.IsEmpty(
			violations,
			"Shared support files:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void ATestSupportFolderHoldsNoTestClassExceptAnAssemblyInitializer()
	{
		List<string> violations = [];

		foreach (TestType type in s_types.Value.Where(type => type.IsTestClass && !type.HasAssemblyInitializer && IsInSupportFolder(type.Path)))
			violations.Add($"{type.Path} declares the test class {type.Name} in a TestSupport folder.");

		Assert.IsEmpty(
			violations,
			"Support test classes:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void ATypeThatIsNoTestClassTakesANameFromTheTestVocabulary()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (
			TestType type in s_types.Value.Where(type =>
				!type.IsTestClass
				&& !type.Name.EndsWith("TestsBase", StringComparison.Ordinal)
				&& !type.Path.StartsWith($"tests/{s_guardProjectName}/", StringComparison.Ordinal)
			)
		)
		{
			string? problem = VocabularyProblem(type);

			if (problem is not null && !s_vocabularyTypes.Allows(type.Path))
				violations.Add(s_vocabularyTypes.Rejection($"{type.Name} ({type.Path}) {problem}", Guard));
		}

		foreach (string message in s_vocabularyTypes.UnmatchedMessages(Guard))
			violations.Add(message);

		Assert.IsEmpty(
			violations,
			"Test vocabulary:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void TheTestOfAnInternalOnlyTypeSitsInTheMirroredFolder()
	{
		List<string> violations = [];

		foreach (string folder in RepositoryModel.InternalOnlyFolders)
		{
			if (!TryMirror(folder, out TestMirror? mirror, out string? problem))
			{
				violations.Add($"{folder}: {problem}");
				continue;
			}

			HashSet<string> sourceTypes =
			[
				.. Directory
					.EnumerateFiles(RepositoryPaths.Below(folder.Split('/')), "*.cs")
					.Select(path => Path.GetFileNameWithoutExtension(path).Split('.')[0]),
			];

			foreach (string path in RepositoryModel.SourcePathsUnder([.. mirror.Searched]))
			{
				string testName = Path.GetFileNameWithoutExtension(path).Split('.')[0];

				if (!testName.EndsWith("Tests", StringComparison.Ordinal) || !sourceTypes.Contains(testName[..^"Tests".Length]))
					continue;

				string directory = Path.GetDirectoryName(path)!.Replace('\\', '/');

				if (!mirror.Mirrored.Contains(directory, StringComparer.Ordinal))
					violations.Add($"{path} tests a type of {folder}, so it belongs in {string.Join(" or ", mirror.Mirrored)}.");
			}
		}

		Assert.IsEmpty(
			violations,
			"Test placement:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	/// <summary>
	/// Resolves where the tests of a folder of internal-only types sit. A folder of a library has the mirrored folder of the library's
	/// own test project, found through the project that owns the folder by its directory. A folder of a shared home has the mirrored folder
	/// of each shared test tree that the test projects of the projects compiling the home include.
	/// </summary>
	private static bool TryMirror(string folder, [NotNullWhen(true)] out TestMirror? mirror, [NotNullWhen(false)] out string? problem)
	{
		mirror = null;
		problem = null;

		SharedHome? home = RepositoryModel.SharedHomeOf(folder + "/");

		if (home is null)
		{
			ProjectFile? owner = RepositoryModel
				.SourceProjects.Where(project => folder.StartsWith(project.RelativeDirectory + "/", StringComparison.Ordinal))
				.MaxBy(project => project.RelativeDirectory.Length);

			if (owner is null)
			{
				problem = "sits in no library project and no shared home, so no test project mirrors it.";
				return false;
			}

			if (!RepositoryModel.HasProject(owner.Name + ".Tests"))
			{
				problem = $"belongs to {owner.Name}, which has no test project {owner.Name}.Tests to mirror it.";
				return false;
			}

			ProjectFile test = RepositoryModel.Project(owner.Name + ".Tests");

			mirror = new TestMirror([test.RelativeDirectory], [test.RelativeDirectory + "/" + folder[(owner.RelativeDirectory.Length + 1)..]]);
			return true;
		}

		string below = folder.Length > home.Folder.Length ? folder[(home.Folder.Length + 1)..] : string.Empty;
		List<ProjectFile> tests = [];

		foreach (
			ProjectFile compiling in RepositoryModel.SourceProjects.Where(project =>
				project.Sources.Any(source => source.StartsWith(home.Folder + "/", StringComparison.Ordinal))
			)
		)
		{
			if (!RepositoryModel.HasProject(compiling.Name + ".Tests"))
			{
				problem = $"is compiled by {compiling.Name}, which has no test project {compiling.Name}.Tests to mirror it.";
				return false;
			}

			tests.Add(RepositoryModel.Project(compiling.Name + ".Tests"));
		}

		string[] trees =
		[
			.. tests
				.SelectMany(test => test.Sources)
				.Select(SharedTreeOf)
				.OfType<string>()
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

		if (tests.Count == 0 || trees.Length == 0)
		{
			problem = $"belongs to {home.Folder}, which no test project's shared test tree covers, so no test tree mirrors it.";
			return false;
		}

		mirror = new TestMirror(
			[.. trees, .. tests.Select(test => test.RelativeDirectory)],
			[.. trees.Select(tree => below.Length == 0 ? tree : tree + "/" + below)]
		);

		return true;
	}

	/// <summary>
	/// The shared test tree a source path sits in, as the folder of the tree, or <see langword="null"/> when the path sits in none.
	/// </summary>
	private static string? SharedTreeOf(string source)
	{
		foreach (string root in RepositoryModel.SharedTreeFolders)
		{
			if (source.StartsWith(root + "/", StringComparison.Ordinal))
				return root + "/" + source[(root.Length + 1)..].Split('/')[0];
		}

		return null;
	}

	[TestMethod]
	public void ATestFolderNamesASourceFolderOfTheProjectItTests()
	{
		List<string> violations = [];

		foreach (
			ProjectFile test in RepositoryModel.Projects.Where(project =>
				project.IsTestProject && project.Name.EndsWith(".Tests", StringComparison.Ordinal)
			)
		)
		{
			if (!RepositoryModel.HasProject(test.Name[..^".Tests".Length]))
				continue;

			ProjectFile tested = RepositoryModel.Project(test.Name[..^".Tests".Length]);
			string testDirectory = RepositoryPaths.Below(test.RelativeDirectory.Split('/'));
			HashSet<string> sourceFolders = SourceFolderNames(tested);

			foreach (
				string folder in Directory.EnumerateDirectories(testDirectory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)
			)
			{
				if (folder is "bin" or "obj" or "Properties" or SupportFolder or "TestResults" || sourceFolders.Contains(folder))
					continue;

				if (!s_testFolders.Allows($"{test.Name}/{folder}"))
					violations.Add(s_testFolders.Rejection($"{test.RelativeDirectory}/{folder} names no source folder of {tested.Name}.", Guard));
			}
		}

		violations.AddRange(s_testFolders.UnmatchedMessages(Guard));

		Assert.IsEmpty(
			violations,
			"Test folders:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	/// <summary>The names of the folders of a project's own directory, at any depth, and of the shared homes it compiles.</summary>
	private static HashSet<string> SourceFolderNames(ProjectFile project)
	{
		HashSet<string> names = new(StringComparer.Ordinal);

		foreach (string path in project.Sources)
		{
			string[] segments = path.Split('/');

			foreach (string folder in segments[..^1])
				names.Add(folder);
		}

		return names;
	}

	/// <summary>The reason a type breaks the vocabulary, or <see langword="null"/> when its name is a word of the table.</summary>
	private static string? VocabularyProblem(TestType type)
	{
		string name = type.Name;

		if (VocabularyTable.RoleWords.Any(word => word is "Attribute" or "Exception" or "EventArgs" && name.EndsWith(word, StringComparison.Ordinal)))
			return null;

		if (VocabularyTable.TestSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
			return null;

		string? prefix = VocabularyTable.TestDoublePrefixes.FirstOrDefault(word =>
			name.StartsWith(word, StringComparison.Ordinal) && name.Length > word.Length && char.IsUpper(name[word.Length])
		);

		if (prefix is null)
			return "starts with none of the test double words and ends with none of the test helper suffixes of the vocabulary table.";

		if (prefix != "Test")
			return null;

		// Test marks a subclass written for a test, and a static class named for the one kind of thing it provides or runs.
		if (type.IsStatic)
			return null;

		return type.FirstBase is { } first && !IsInterfaceName(first)
			? null
			: "starts with Test, which the vocabulary reserves for a subclass and for a static class named for the one kind of thing it provides or runs.";
	}

	private static bool IsInterfaceName(string name) => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);

	private static bool IsInSupportFolder(string path) => path.Split('/')[..^1].Contains(SupportFolder, StringComparer.Ordinal);

	/// <summary>Reports whether a path sits in <c>tests/TestSupport</c> or in the <c>TestSupport</c> folder of a family below <c>tests</c>.</summary>
	private static bool IsSharedSupportFolder(string[] segments)
	{
		bool inFamilyFolder =
			segments.Length > 2
			&& segments[0] == "tests"
			&& segments[2] == SupportFolder
			&& !segments[1].StartsWith("Nickelony.", StringComparison.Ordinal);

		return segments.Length > 2 && segments[0] == "tests" && (segments[1] == SupportFolder || inFamilyFolder);
	}

	private static IReadOnlyList<TestType> ReadTypes()
	{
		List<TestType> parts = [];

		foreach (string path in RepositoryModel.SourcePathsUnder("tests"))
		{
			foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch([]))
			{
				foreach (
					BaseTypeDeclarationSyntax type in tree.GetRoot()
						.DescendantNodes()
						.OfType<BaseTypeDeclarationSyntax>()
						.Where(type => type.Parent is not BaseTypeDeclarationSyntax)
				)
				{
					string? ns = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();

					bool attributed = type
						.AttributeLists.SelectMany(list => list.Attributes)
						.Any(attribute => IsTestClassAttribute(attribute.Name.ToString()));

					bool initializer = type.DescendantNodes()
						.OfType<MethodDeclarationSyntax>()
						.Any(method =>
							method
								.AttributeLists.SelectMany(list => list.Attributes)
								.Any(attribute => attribute.Name.ToString().EndsWith("AssemblyInitialize", StringComparison.Ordinal))
						);

					string? first = (type as TypeDeclarationSyntax)?.BaseList?.Types.FirstOrDefault()?.Type is { } baseType
						? SimpleName(baseType)
						: null;

					parts.Add(
						new TestType(
							path,
							Name: type.Identifier.ValueText,
							Namespace: ns,
							IsTestClass: attributed,
							HasAssemblyInitializer: initializer,
							IsStatic: type.Modifiers.Any(SyntaxKind.StaticKeyword),
							FirstBase: first
						)
					);
				}
			}
		}

		// A partial class states its attribute on any of its parts, so each part of a type takes the union.
		HashSet<string> testClasses = [.. parts.Where(part => part.IsTestClass).Select(part => $"{part.Namespace}.{part.Name}")];
		HashSet<string> initializers = [.. parts.Where(part => part.HasAssemblyInitializer).Select(part => $"{part.Namespace}.{part.Name}")];

		return
		[
			.. parts
				.Select(part =>
					part with
					{
						IsTestClass = testClasses.Contains($"{part.Namespace}.{part.Name}"),
						HasAssemblyInitializer = initializers.Contains($"{part.Namespace}.{part.Name}"),
					}
				)
				.DistinctBy(part => (part.Path, part.Name)),
		];
	}

	private static bool IsTestClassAttribute(string name) =>
		name.EndsWith("TestClass", StringComparison.Ordinal) || name.EndsWith("TestClassAttribute", StringComparison.Ordinal);

	private static string SimpleName(TypeSyntax type)
	{
		return type switch
		{
			QualifiedNameSyntax qualified => SimpleName(qualified.Right),
			SimpleNameSyntax simple => simple.Identifier.ValueText,
			_ => type.ToString(),
		};
	}

	/// <summary>The folders whose test sources a mirror check reads, and the folders in which the tests of a type may sit.</summary>
	private sealed record TestMirror(IReadOnlyList<string> Searched, IReadOnlyList<string> Mirrored);

	private sealed record TestType(
		string Path,
		string Name,
		string? Namespace,
		bool IsTestClass,
		bool HasAssemblyInitializer,
		bool IsStatic,
		string? FirstBase
	);
}
