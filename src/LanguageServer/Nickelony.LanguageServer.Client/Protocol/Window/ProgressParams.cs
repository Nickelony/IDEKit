namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a <c>$/progress</c> notification that reports the progress of one unit of work.</summary>
/// <param name="Token">
/// The token that ties the notification to the progress the server announced, whether the server spelled it as a string or an integer.
/// </param>
/// <param name="Value">The report the notification carries.</param>
/// <remarks>
/// The client parses the notification by hand, because the token is a string or an integer and the value is a progress report
/// only for a work-done token, so a notification that carries any other value is not surfaced.
/// </remarks>
public sealed record ProgressParams(string Token, WorkDoneProgressPayload Value);

/// <summary>Represents one work-done progress report: the start of the work, a step of it, or its end.</summary>
/// <param name="Kind">The report kind as the protocol spells it: <c>begin</c>, <c>report</c> or <c>end</c>.</param>
/// <param name="Title">The title of the work, which a <c>begin</c> report carries, or <see langword="null"/> when the report has none.</param>
/// <param name="Message">The detail of the current step, or <see langword="null"/> when the report has none.</param>
/// <param name="Percentage">The completed share from 0 to 100, or <see langword="null"/> when the work reports none.</param>
public sealed record WorkDoneProgressPayload(string Kind, string? Title, string? Message, int? Percentage);
