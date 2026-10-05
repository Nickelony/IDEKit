using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Roslyn.Tests;

[TestClass]
public sealed class ProjectLoadNotificationBuilderTests
{
	private static readonly RoslynLanguageDescriptor s_csharp = RoslynLanguageDescriptor.For(RoslynLanguage.CSharp);

	[TestMethod]
	public void Build_ForARootWithOneSolution_OpensTheSolution()
	{
		using var root = new TemporaryDirectory("roslyn-solution-");
		string solutionPath = CreateFile(root.Path, "App.slnx");
		CreateFile(root.Path, "src", "App", "App.csproj");

		SessionStartNotification notification = Assert.ContainsSingle(
			ProjectLoadNotificationBuilder.Build(s_csharp, RoslynLanguageServerOptions.Default, [root.Path])
		);

		Assert.AreEqual("solution/open", notification.Method);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(solutionPath), ReadParameters(notification)["solution"]?.GetValue<string>());
	}

	[TestMethod]
	public void Build_ForARootWithProjectsOnly_OpensEveryProjectOfTheLanguageAndSkipsBuildAndHiddenFolders()
	{
		using var root = new TemporaryDirectory("roslyn-projects-");

		string app = CreateFile(root.Path, "App", "App.csproj");
		string library = CreateFile(root.Path, "src", "Library", "Library.csproj");

		CreateFile(root.Path, "Legacy", "Legacy.vbproj");
		CreateFile(root.Path, "bin", "Debug", "Copied.csproj");
		CreateFile(root.Path, "obj", "Generated.csproj");
		CreateFile(root.Path, "node_modules", "package", "Package.csproj");
		CreateFile(root.Path, ".hidden", "Hidden.csproj");

		SessionStartNotification notification = Assert.ContainsSingle(
			ProjectLoadNotificationBuilder.Build(s_csharp, RoslynLanguageServerOptions.Default, [root.Path])
		);

		Assert.AreEqual("project/open", notification.Method);

		Assert.AreSequenceEqual([DocumentUriConversion.CreateFileUri(app), DocumentUriConversion.CreateFileUri(library)], ReadProjects(notification));
	}

	[TestMethod]
	public void Build_ForARootWithTwoSolutions_OpensTheProjectsInstead()
	{
		using var root = new TemporaryDirectory("roslyn-two-solutions-");

		CreateFile(root.Path, "App.sln");
		CreateFile(root.Path, "App.slnx");

		string app = CreateFile(root.Path, "App", "App.csproj");

		SessionStartNotification notification = Assert.ContainsSingle(
			ProjectLoadNotificationBuilder.Build(s_csharp, RoslynLanguageServerOptions.Default, [root.Path])
		);

		Assert.AreEqual("project/open", notification.Method);
		Assert.AreSequenceEqual([DocumentUriConversion.CreateFileUri(app)], ReadProjects(notification));
	}

	[TestMethod]
	public void Build_ForVisualBasic_OpensTheVisualBasicProjects()
	{
		using var root = new TemporaryDirectory("roslyn-vb-");
		CreateFile(root.Path, "App", "App.csproj");
		string legacy = CreateFile(root.Path, "Legacy", "Legacy.vbproj");

		SessionStartNotification notification = Assert.ContainsSingle(
			ProjectLoadNotificationBuilder.Build(
				RoslynLanguageDescriptor.For(RoslynLanguage.VisualBasic),
				RoslynLanguageServerOptions.Default,
				[root.Path]
			)
		);

		Assert.AreSequenceEqual([DocumentUriConversion.CreateFileUri(legacy)], ReadProjects(notification));
	}

	[TestMethod]
	public void Build_WithAMaximum_CapsTheProjectsAcrossTheRootsShallowestFirst()
	{
		using var first = new TemporaryDirectory("roslyn-cap-first-");
		using var second = new TemporaryDirectory("roslyn-cap-second-");

		string shallow = CreateFile(first.Path, "Shallow.csproj");

		CreateFile(first.Path, "deep", "Deep.csproj");
		CreateFile(second.Path, "Other.csproj");

		var options = new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 1 };

		SessionStartNotification notification = Assert.ContainsSingle(
			ProjectLoadNotificationBuilder.Build(s_csharp, options, [first.Path, second.Path])
		);

		Assert.AreSequenceEqual([DocumentUriConversion.CreateFileUri(shallow)], ReadProjects(notification));
	}

	[TestMethod]
	public void Build_WithAZeroMaximum_SendsNothing()
	{
		using var root = new TemporaryDirectory("roslyn-disabled-");
		CreateFile(root.Path, "App.sln");

		var options = new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 0 };

		Assert.IsEmpty(ProjectLoadNotificationBuilder.Build(s_csharp, options, [root.Path]));
	}

	[TestMethod]
	public void Build_ForRootsWithoutProjectsOrThatDoNotExist_SendsNothing()
	{
		using var root = new TemporaryDirectory("roslyn-empty-");
		CreateFile(root.Path, "Program.cs");

		string missing = Path.Combine(root.Path, "missing");

		Assert.IsEmpty(ProjectLoadNotificationBuilder.Build(s_csharp, RoslynLanguageServerOptions.Default, [root.Path, missing]));
		Assert.IsEmpty(ProjectLoadNotificationBuilder.Build(s_csharp, RoslynLanguageServerOptions.Default, []));
	}

	[TestMethod]
	public void Profile_BuildsTheSessionStartNotificationsOfItsLanguage()
	{
		using var root = new TemporaryDirectory("roslyn-profile-");
		string solutionPath = CreateFile(root.Path, "App.sln");

		LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(RoslynLanguage.CSharp);

		Assert.IsNotNull(profile.Hooks.BuildSessionStartNotifications);

		SessionStartNotification notification = Assert.ContainsSingle(profile.Hooks.BuildSessionStartNotifications([root.Path]));

		Assert.AreEqual("solution/open", notification.Method);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(solutionPath), ReadParameters(notification)["solution"]?.GetValue<string>());
	}

	private static string CreateFile(string root, params string[] relativeParts)
	{
		string path = Path.Combine([root, .. relativeParts]);

		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
		File.WriteAllText(path, string.Empty);

		return path;
	}

	private static JsonObject ReadParameters(SessionStartNotification notification) =>
		JsonSerializer.SerializeToNode(notification.Parameters)?.AsObject() ?? throw new AssertFailedException("The notification has no parameters.");

	private static string[] ReadProjects(SessionStartNotification notification) =>
		[.. (ReadParameters(notification)["projects"]?.AsArray() ?? []).Select(project => project?.GetValue<string>() ?? string.Empty)];
}
