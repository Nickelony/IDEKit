namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies that the real launcher maps environment variables (including an override of an inherited variable),
/// the working directory, the argument list, and standard-error redirection onto a live child process.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
public class ProcessLauncherTests
{
	private const int WaitTimeoutMilliseconds = 10_000;

	[TestMethod]
	public void Start_PassesEnvironmentVariables_ChildSeesTheValue()
	{
		var runner = new ProcessRunner();
		using IProcessHandle handle = runner.Start(new ProcessRunRequest
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.EchoEnvironmentVariable("NICKELONY_PROCESSES_TEST"),
			EnvironmentVariables = new Dictionary<string, string> { ["NICKELONY_PROCESSES_TEST"] = "environment-value" },
			RedirectStandardOutput = true
		});

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.AreEqual("environment-value", handle.StandardOutput.Trim());
	}

	[TestMethod]
	public void Start_UsesWorkingDirectory_ChildReportsIt()
	{
		string directory = Path.Combine(Path.GetTempPath(), "NickelonyProcessesTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			var runner = new ProcessRunner();
			using IProcessHandle handle = runner.Start(new ProcessRunRequest
			{
				FileName = TestCommands.ShellFileName,
				RawArguments = TestCommands.PrintWorkingDirectory(),
				WorkingDirectory = directory,
				RedirectStandardOutput = true
			});

			Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));

			char separator = Path.DirectorySeparatorChar;
			string expected = directory.TrimEnd(separator);
			string actual = handle.StandardOutput.Trim().TrimEnd(separator);

			// Directory paths are compared case-insensitively on Windows and case-sensitively elsewhere. macOS
			// reports the physical path of TMPDIR (for example /private/var/...), so the canonical /private
			// prefix is accepted there as well.
			bool matches = string.Equals(
					expected,
					actual,
					OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
				|| (OperatingSystem.IsMacOS() && string.Equals("/private" + expected, actual, StringComparison.Ordinal));

			Assert.IsTrue(matches, $"The child reported '{actual}' instead of '{expected}'.");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	public void Start_PassesArgumentList_ChildReceivesSpacedArgument()
	{
		var runner = new ProcessRunner();
		using IProcessHandle handle = runner.Start(new ProcessRunRequest
		{
			FileName = TestCommands.ShellFileName,
			ArgumentList = TestCommands.EchoSingleArgument("hello world"),
			RedirectStandardOutput = true
		});

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		StringAssert.Contains(handle.StandardOutput, "hello world");
	}

	[TestMethod]
	public void Start_RedirectsStandardError_ReturnsCapturedText()
	{
		var runner = new ProcessRunner();
		using IProcessHandle handle = runner.Start(new ProcessRunRequest
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.WriteToStandardError("problem"),
			RedirectStandardError = true
		});

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.AreEqual("problem", handle.StandardError.Trim());
	}

	[TestMethod]
	public void Start_ReplacesInheritedVariable_ChildSeesTheOverride()
	{
		// PATH is inherited by the test process, so the child proves the override replaced the inherited value
		// instead of being ignored or appended.
		var runner = new ProcessRunner();
		using IProcessHandle handle = runner.Start(new ProcessRunRequest
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.EchoEnvironmentVariable("PATH"),
			EnvironmentVariables = new Dictionary<string, string> { ["PATH"] = "overridden-path" },
			RedirectStandardOutput = true
		});

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.AreEqual("overridden-path", handle.StandardOutput.Trim());
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Start_ReplacesInheritedVariableCaseInsensitivelyOnWindows()
	{
		var runner = new ProcessRunner();
		using IProcessHandle handle = runner.Start(new ProcessRunRequest
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.EchoEnvironmentVariable("PATH"),
			EnvironmentVariables = new Dictionary<string, string> { ["Path"] = "case-overridden-path" },
			RedirectStandardOutput = true
		});

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.AreEqual("case-overridden-path", handle.StandardOutput.Trim());
	}
}
