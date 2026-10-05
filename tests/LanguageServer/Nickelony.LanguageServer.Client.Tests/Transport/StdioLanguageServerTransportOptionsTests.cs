namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class StdioLanguageServerTransportOptionsTests
{
	[TestMethod]
	public void Default_HasTheDocumentedDefaults()
	{
		StdioLanguageServerTransportOptions defaults = StdioLanguageServerTransportOptions.Default;

		Assert.IsEmpty(defaults.ServerArguments);
		Assert.IsNull(defaults.ServerWorkingDirectory);
		Assert.IsEmpty(defaults.EnvironmentVariables);
		Assert.AreSame(StringComparer.Ordinal, defaults.EnvironmentVariableNameComparer);
		Assert.IsNull(defaults.ChildProcessLifetimeGuardFactory);
	}

	[TestMethod]
	public void ServerArgumentsAndEnvironmentVariables_AreCopiedAndNormalized()
	{
		var arguments = new List<string> { "--log=debug" };
		var variables = new Dictionary<string, string> { ["VAR"] = "value" };

		StdioLanguageServerTransportOptions configured = StdioLanguageServerTransportOptions.Default with
		{
			ServerArguments = arguments,
			EnvironmentVariables = variables,
		};

		arguments.Add("--appended-after-assignment");
		variables["VAR"] = "changed-after-assignment";

		Assert.HasCount(1, configured.ServerArguments);
		Assert.AreEqual("--log=debug", configured.ServerArguments[0]);
		Assert.AreEqual("value", configured.EnvironmentVariables["VAR"]);

		StdioLanguageServerTransportOptions nulled = StdioLanguageServerTransportOptions.Default with
		{
			// The options normalize null collections to empty instances.
			ServerArguments = null!,
			EnvironmentVariables = null!,
		};

		Assert.IsEmpty(nulled.ServerArguments);
		Assert.IsEmpty(nulled.EnvironmentVariables);
	}

	[TestMethod]
	public void ServerArgumentsWithNullEntry_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => StdioLanguageServerTransportOptions.Default with { ServerArguments = ["--log=debug", null!] });

	[TestMethod]
	public void EnvironmentVariablesWithCasingOnlyDuplicates_UseTheConfiguredComparer()
	{
		var variables = new Dictionary<string, string> { ["VAR"] = "first", ["var"] = "second" };

		// The default ordinal comparer is host-neutral, so the two spellings stay distinct variables.
		StdioLanguageServerTransportOptions caseSensitive = StdioLanguageServerTransportOptions.Default with
		{
			EnvironmentVariables = variables,
		};

		Assert.HasCount(2, caseSensitive.EnvironmentVariables);

		// A host on a case-insensitive platform opts in explicitly, which rejects sibling entries that differ only
		// in casing instead of letting one silently overwrite the other in the child process environment.
		Assert.ThrowsExactly<ArgumentException>(() =>
			StdioLanguageServerTransportOptions.Default with
			{
				EnvironmentVariableNameComparer = StringComparer.OrdinalIgnoreCase,
				EnvironmentVariables = variables,
			}
		);

		// The comparer re-validates variables that were assigned before it, so the assignment order cannot change the outcome.
		Assert.ThrowsExactly<ArgumentException>(() =>
			StdioLanguageServerTransportOptions.Default with
			{
				EnvironmentVariables = variables,
				EnvironmentVariableNameComparer = StringComparer.OrdinalIgnoreCase,
			}
		);
	}

	[TestMethod]
	public void ServerWorkingDirectory_RejectsBlankValues()
	{
		Assert.ThrowsExactly<ArgumentException>(() => StdioLanguageServerTransportOptions.Default with { ServerWorkingDirectory = " " });

		Assert.AreEqual(
			TestPaths.Root,
			(StdioLanguageServerTransportOptions.Default with { ServerWorkingDirectory = TestPaths.Root }).ServerWorkingDirectory
		);
	}

	[TestMethod]
	public void EnvironmentVariableNameComparer_RejectsNull()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			StdioLanguageServerTransportOptions.Default with
			{
				EnvironmentVariableNameComparer = null!,
			}
		);
	}
}
