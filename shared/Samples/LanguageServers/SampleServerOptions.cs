using System.IO;

namespace Nickelony.Samples;

/// <summary>The language servers the sample starts, each named by an environment variable so that the build and the tests never need one.</summary>
/// <param name="LuaServerPath">
/// The path of the Lua language server executable or of a LuaLS <c>.zip</c> archive, or <see langword="null"/> to use the in-process providers.
/// </param>
/// <param name="RoslynServerPath">
/// The path of the Roslyn language server executable, or <see langword="null"/> to leave the C# and Visual Basic files without language features.
/// </param>
internal sealed record SampleServerOptions(string? LuaServerPath, string? RoslynServerPath)
{
	internal const string LuaVariable = "NICKELONY_SAMPLE_LUA_SERVER";
	internal const string RoslynVariable = "NICKELONY_SAMPLE_ROSLYN_SERVER";

	// The Lua integration tests read their archive from this variable, so one setting serves both.
	internal const string LuaArchiveVariable = "NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE";

	private const string RoslynExecutableName = "Microsoft.CodeAnalysis.LanguageServer.exe";
	private const string VisualBasicAssemblyName = "Microsoft.CodeAnalysis.VisualBasic.dll";

	/// <summary>No language server: the in-process providers answer.</summary>
	internal static SampleServerOptions Default { get; } = new(null, null);

	/// <summary>Reads the server paths from the environment, treating an unset or blank variable as no server.</summary>
	internal static SampleServerOptions FromEnvironment() => new(Read(LuaVariable) ?? Read(LuaArchiveVariable), Read(RoslynVariable));

	/// <summary>Finds the Roslyn language server that the C# extension of Visual Studio Code installs, or returns <see langword="null"/>.</summary>
	/// <remarks>
	/// The newest extension wins; the file picker of the Language menu starts there, so trying Roslyn takes no download.
	/// </remarks>
	internal static string? FindInstalledRoslynServer()
	{
		string extensions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");

		if (!Directory.Exists(extensions))
			return null;

		return Directory
			.EnumerateDirectories(extensions, "ms-dotnettools.csharp-*")
			.Select(extension => Path.Combine(extension, ".roslyn", RoslynExecutableName))
			.Where(File.Exists)
			.OrderDescending(StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
	}

	/// <summary>Whether a build of the Roslyn server carries Visual Basic, which the build that Visual Studio Code installs leaves out.</summary>
	internal static bool ServesVisualBasic(string roslynServerPath) =>
		Path.GetDirectoryName(Path.GetFullPath(roslynServerPath)) is { } folder && File.Exists(Path.Combine(folder, VisualBasicAssemblyName));

	private static string? Read(string name)
	{
		string? value = Environment.GetEnvironmentVariable(name);
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}
}
