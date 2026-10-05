namespace Nickelony.LanguageServer.Provider;

/// <summary>A report of the progress of one unit of work that the language server runs, such as loading the projects of a workspace.</summary>
/// <param name="Token">The identity of the unit of work, which its reports share.</param>
/// <param name="Kind">Whether the report starts the work, updates it or ends it.</param>
/// <param name="Title">The title of the work, which the report that starts it carries, or <see langword="null"/> when the report has none.</param>
/// <param name="Message">The detail of the current step, or <see langword="null"/> when the report has none.</param>
/// <param name="Percentage">The completed share from 0 to 100, or <see langword="null"/> when the work reports none.</param>
public sealed record WorkDoneProgress(string Token, WorkDoneProgressKind Kind, string? Title, string? Message, int? Percentage);

/// <summary>Classifies a <see cref="WorkDoneProgress"/> report within the life of its unit of work.</summary>
public enum WorkDoneProgressKind
{
	/// <summary>The report starts the work.</summary>
	Begin,

	/// <summary>The report updates the work.</summary>
	Report,

	/// <summary>The report ends the work.</summary>
	End,
}
