using System.ComponentModel;
using System.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using EditorDiagnosticSeverity = Nickelony.IDEKit.Core.Diagnostics.DiagnosticSeverity;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>Live tests against a developer-supplied Roslyn language server.</summary>
/// <remarks>
/// The server is supplied by the developer, so the tests are inconclusive when <c>NICKELONY_ROSLYN_LANGUAGE_SERVER</c> is unset or names no file,
/// and when no <c>dotnet</c> SDK answers <c>dotnet --version</c>, because the scratch project targets the SDK's own framework.
/// </remarks>
[TestClass]
[TestCategory(TestCategories.Integration)]
public sealed class RoslynLanguageServerRealIntegrationTests
{
	private const string ServerVariable = "NICKELONY_ROSLYN_LANGUAGE_SERVER";
	private const string TypeErrorCode = "CS0029";
	private const string SyntaxErrorCode = "CS1002";

	private const string TypeErrorSource = "class Probe\r\n{\r\n    int Value = \"text\";\r\n}\r\n";
	private const string TypeAndSyntaxErrorSource = "class Probe\r\n{\r\n    int Value = \"text\";\r\n    int Broken = 1\r\n}\r\n";

	private static readonly TimeSpan s_diagnosticsTimeout = TimeSpan.FromSeconds(30);

	// A loaded project reports its compiler errors well inside this window, so a diagnostic that stays absent for it is absent because no project loaded.
	private static readonly TimeSpan s_noProjectWindow = TimeSpan.FromSeconds(10);

	[TestMethod]
	public async Task Provider_WithRealRoslynLanguageServer_ReportsASeededCompilerErrorThroughPullDiagnosticsOnceTheProjectLoads()
	{
		using var session = new RealRoslynLanguageServerTestSession();
		using var logger = new RecordingLogger();

		string filePath = session.WriteSource(TypeErrorSource);

		using var provider = new LanguageServerFeatureProvider(
			profile: RoslynLanguageServerProfileFactory.Create(RoslynLanguage.CSharp),
			workspaceRootDirectoryPaths: [session.WorkspaceRoot],
			serverExecutablePath: session.ExecutablePath,
			logger: logger
		);

		provider.OpenDocument(filePath, TypeErrorSource);

		IReadOnlyList<Diagnostic> diagnostics = await TestPolling.WaitForAsync(
			action: () => Task.FromResult(provider.GetDiagnostics(filePath)),
			isSatisfied: reported => reported.Any(diagnostic => diagnostic.Code == TypeErrorCode),
			timeout: s_diagnosticsTimeout,
			failureMessage: "Expected the project to load and the server to report the seeded type error through pull diagnostics.",
			describeLastResult: reported => Describe(reported, logger)
		);

		Diagnostic typeError = diagnostics.First(diagnostic => diagnostic.Code == TypeErrorCode);

		Assert.AreEqual(EditorDiagnosticSeverity.Error, typeError.Severity);
		Assert.AreEqual("\"text\"", typeError.Range.GetText(TypeErrorSource));
		Assert.IsFalse(string.IsNullOrWhiteSpace(typeError.Message));
	}

	[TestMethod]
	public async Task Provider_WithRealRoslynLanguageServerAndAutoLoadDisabled_ReportsASyntaxErrorButLoadsNoProject()
	{
		using var session = new RealRoslynLanguageServerTestSession();
		using var logger = new RecordingLogger();

		string filePath = session.WriteSource(TypeAndSyntaxErrorSource);

		// A zero maximum omits the switch, which the server rejects as a value, and sends no project to open.
		var options = new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 0 };

		using var provider = new LanguageServerFeatureProvider(
			profile: RoslynLanguageServerProfileFactory.Create(RoslynLanguage.CSharp, options),
			workspaceRootDirectoryPaths: [session.WorkspaceRoot],
			serverExecutablePath: session.ExecutablePath,
			logger: logger
		);

		provider.OpenDocument(filePath, TypeAndSyntaxErrorSource);

		await TestPolling.WaitForAsync(
			action: () => Task.FromResult(provider.GetDiagnostics(filePath)),
			isSatisfied: reported => reported.Any(diagnostic => diagnostic.Code == SyntaxErrorCode),
			timeout: s_diagnosticsTimeout,
			failureMessage: "Expected the server to start and to report the seeded syntax error without a project.",
			describeLastResult: reported => Describe(reported, logger)
		);

		bool typeErrorReported = await TestPolling.ForConditionAsync(
			() => provider.GetDiagnostics(filePath).Any(diagnostic => diagnostic.Code == TypeErrorCode),
			s_noProjectWindow
		);

		Assert.IsFalse(typeErrorReported, "A file in no loaded project reports no compiler error of its types.");
	}

	private static string Describe(IReadOnlyList<Diagnostic> diagnostics, RecordingLogger logger)
	{
		return "Last diagnostic codes: "
			+ string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Code))
			+ Environment.NewLine
			+ "Provider log:"
			+ Environment.NewLine
			+ string.Join(Environment.NewLine, logger.Logs);
	}

	private sealed class RealRoslynLanguageServerTestSession : IDisposable
	{
		private readonly TemporaryDirectory _workspace;

		public RealRoslynLanguageServerTestSession()
		{
			string? serverPath = Environment.GetEnvironmentVariable(ServerVariable);

			// The repository ships no Roslyn language server, so the developer points this variable at one;
			// the tier stays opt-in rather than silently passing.
			if (string.IsNullOrWhiteSpace(serverPath) || !File.Exists(serverPath))
			{
				throw new AssertInconclusiveException(
					$"The Roslyn integration fixture is not configured. Set {ServerVariable} to the path of "
						+ "Microsoft.CodeAnalysis.LanguageServer.exe; this repository does not provide one."
				);
			}

			ExecutablePath = serverPath;
			_workspace = new TemporaryDirectory("RoslynWorkspace_");

			try
			{
				File.WriteAllText(
					_workspace.GetFilePath("Probe.csproj"),

					"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
						+ $"<TargetFramework>{FindSdkTargetFramework(_workspace.Path)}</TargetFramework>"
						+ "<OutputType>Library</OutputType></PropertyGroup></Project>"
				);
			}
			catch
			{
				// A constructor that throws never reaches Dispose, so the scratch folder is removed here.
				_workspace.Dispose();
				throw;
			}
		}

		public string ExecutablePath { get; }
		public string WorkspaceRoot => _workspace.Path;

		/// <summary>Writes <paramref name="source"/> into the scratch project and returns the file's path.</summary>
		public string WriteSource(string source)
		{
			string filePath = _workspace.GetFilePath("Probe.cs");

			File.WriteAllText(filePath, source);
			return filePath;
		}

		public void Dispose() => _workspace.Dispose();

		/// <summary>Returns the target framework of the SDK that <c>dotnet</c> selects for <paramref name="workingDirectory"/>.</summary>
		/// <remarks>
		/// The server's build host uses the same SDK, and its targeting pack is the one that restores without a network.
		/// </remarks>
		private static string FindSdkTargetFramework(string workingDirectory)
		{
			var startInfo = new ProcessStartInfo("dotnet", "--version")
			{
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			try
			{
				using Process process =
					Process.Start(startInfo)
					?? throw new AssertInconclusiveException("The dotnet command could not be started to find the SDK version.");

				string version = process.StandardOutput.ReadToEnd().Trim();

				process.WaitForExit();

				int firstDot = version.IndexOf('.', StringComparison.Ordinal);

				if (process.ExitCode != 0 || firstDot <= 0 || !int.TryParse(version.AsSpan(0, firstDot), out int major))
					throw new AssertInconclusiveException($"The dotnet command did not report an SDK version: '{version}'.");

				return $"net{major}.0";
			}
			catch (Win32Exception exception)
			{
				throw new AssertInconclusiveException("The dotnet command could not be started to find the SDK version: " + exception.Message);
			}
		}
	}
}
