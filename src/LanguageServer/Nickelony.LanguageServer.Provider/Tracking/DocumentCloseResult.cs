namespace Nickelony.LanguageServer.Provider;

/// <summary>Reports what an explicit close did to a tracked document and the snapshot to close the server copy with.</summary>
/// <param name="Outcome">What the close did to the tracked record.</param>
/// <param name="ClosingDocument">
/// The closing snapshot when the outcome is <see cref="DocumentCloseOutcome.Closed"/> and the server copy is open,
/// or <see langword="null"/> otherwise.
/// </param>
/// <remarks>
/// A <see cref="DocumentCloseOutcome.Closed"/> outcome can carry a <see langword="null"/> document: the tracked record was removed, but the server
/// copy was not open (a restart had closed it locally), so no close notification is owed.
/// </remarks>
internal readonly record struct DocumentCloseResult(DocumentCloseOutcome Outcome, DocumentSnapshot? ClosingDocument);
