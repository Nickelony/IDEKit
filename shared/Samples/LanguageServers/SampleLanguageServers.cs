using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.LanguageServer.Lua;
using Nickelony.LanguageServer.Provider;
using Nickelony.LanguageServer.Provider.Abstractions;
using Nickelony.LanguageServer.Roslyn;

namespace Nickelony.Samples;

/// <summary>Creates the Lua and Roslyn language server providers for the sample's folder.</summary>
internal static class SampleLanguageServers
{
	/// <summary>Creates the Lua provider over a server executable.</summary>
	/// <param name="files">The files whose folder is the workspace root.</param>
	/// <param name="executablePath">The path of the <c>lua-language-server</c> executable; see <see cref="LuaServerArchive"/> for an archive.</param>
	/// <param name="logger">Receives the provider's log entries.</param>
	/// <param name="hooks">Receives the messages, the progress and the edits that the server sends besides its answers.</param>
	/// <param name="reportFailure">Receives the message of a failed start.</param>
	/// <remarks>
	/// The provider starts the server lazily, on the first document or request, so constructing it never blocks.
	/// </remarks>
	internal static ILanguageServerFeatureProvider CreateLua(
		SampleFiles files,
		string executablePath,
		ILogger logger,
		LanguageServerFeatureProviderHooks hooks,
		Action<string> reportFailure
	)
	{
		return Observe(
			new LanguageServerFeatureProvider(
				profile: LuaLanguageServerProfileFactory.Create(),
				workspaceRootDirectoryPaths: [files.Root],
				serverExecutablePath: executablePath,
				hooks,
				logger: logger
			),

			reportFailure
		);
	}

	/// <summary>Creates the Roslyn provider of one language over a server executable.</summary>
	/// <param name="files">The files whose folder is the workspace root.</param>
	/// <param name="executablePath">The path of the Roslyn language server executable.</param>
	/// <param name="language">The language the provider serves.</param>
	/// <param name="logger">Receives the provider's log entries.</param>
	/// <param name="hooks">Receives the messages, the progress and the edits that the server sends besides its answers.</param>
	/// <param name="reportFailure">Receives the message of a failed start.</param>
	/// <remarks>
	/// The server serves C# and Visual Basic, but one provider serves one language, so each language starts its own process.
	/// </remarks>
	internal static ILanguageServerFeatureProvider CreateRoslyn(
		SampleFiles files,
		string executablePath,
		RoslynLanguage language,
		ILogger logger,
		LanguageServerFeatureProviderHooks hooks,
		Action<string> reportFailure
	)
	{
		LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(
			language,
			new RoslynLanguageServerOptions { ShowCompletionItemsFromUnimportedNamespaces = false }
		);

		return Observe(
			new LanguageServerFeatureProvider(
				profile,
				workspaceRootDirectoryPaths: [files.Root],
				serverExecutablePath: executablePath,
				hooks,
				logger: logger
			),

			reportFailure
		);
	}

	/// <summary>
	/// Creates the hooks of one language server: its messages and progress go to the status line, and its edits go to the document authority.
	/// </summary>
	/// <param name="serverName">The name that prefixes each status line, for example <c>Roslyn</c>.</param>
	/// <param name="report">Shows a status line; the hooks run on a connection thread, so it takes any thread.</param>
	/// <param name="applyEditAsync">Applies an edit of the server to the documents it names, and says whether it did.</param>
	internal static LanguageServerFeatureProviderHooks CreateHooks(
		string serverName,
		Action<string> report,
		Func<WorkspaceEdit, Task<bool>> applyEditAsync
	)
	{
		var titles = new ConcurrentDictionary<string, string>();

		return new LanguageServerFeatureProviderHooks
		{
			ShowMessage = message => report(DescribeMessage(serverName, message)),
			Progress = progress => report(DescribeProgress(serverName, progress, titles)),
			ApplyWorkspaceEdit = (edit, _) => applyEditAsync(edit),
		};
	}

	/// <summary>Describes a message of a language server as a status line, which names an error as one.</summary>
	/// <param name="serverName">The name that prefixes the line.</param>
	/// <param name="message">The message.</param>
	internal static string DescribeMessage(string serverName, WindowMessage message) =>
		message.Kind == WindowMessageKind.Error ? $"{serverName} error: {message.Text}" : $"{serverName}: {message.Text}";

	/// <summary>Describes a progress report as a status line.</summary>
	/// <param name="serverName">The name that prefixes the line.</param>
	/// <param name="progress">The report.</param>
	/// <param name="titles">
	/// The title of each unit of work that began and has not ended, by token; the reports after the first carry no title.
	/// </param>
	/// <returns>The line: the title, the detail of the step and the percentage when the report has them, or that the work finished.</returns>
	internal static string DescribeProgress(string serverName, WorkDoneProgress progress, ConcurrentDictionary<string, string> titles)
	{
		if (progress.Kind == WorkDoneProgressKind.Begin)
			titles[progress.Token] = progress.Title ?? "Working";

		string title =
			progress.Kind == WorkDoneProgressKind.End
				? titles.TryRemove(progress.Token, out string? ended)
					? ended
					: "Work"
				: titles.GetValueOrDefault(progress.Token, "Working");

		if (progress.Kind == WorkDoneProgressKind.End)
			return $"{serverName}: {title} finished";

		string detail = string.IsNullOrWhiteSpace(progress.Message) ? string.Empty : $" - {progress.Message}";
		string percentage = progress.Percentage is { } value ? $" ({value}%)" : string.Empty;

		return $"{serverName}: {title}{detail}{percentage}";
	}

	private static ILanguageServerFeatureProvider Observe(ILanguageServerFeatureProvider provider, Action<string> reportFailure)
	{
		// The event arrives on a background thread, and the callback marshals to the UI thread.
		provider.StartupFailed += (_, e) => reportFailure(e.Failure.Message);
		return provider;
	}
}
