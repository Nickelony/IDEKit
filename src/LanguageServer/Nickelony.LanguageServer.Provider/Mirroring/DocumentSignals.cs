namespace Nickelony.LanguageServer.Provider;

/// <summary>The reactions of the features that consume what a <see cref="DocumentSynchronizer"/> did to a document.</summary>
/// <param name="Synchronized">Reacts to a document the server now holds in its current state, so a feature starts its refresh for it.</param>
/// <param name="Invalidated">
/// Reacts to a document whose provider-side state moved on from what the work in flight was issued for, so a feature cancels that work.
/// </param>
/// <param name="Renamed">Reacts to a document that is renamed to a new path, so a feature announces the state it renamed with the document.</param>
/// <remarks>
/// The provider that composes the features decides the order in which they react inside each signal.
/// </remarks>
internal sealed record DocumentSignals(Action<DocumentSnapshot> Synchronized, Action<string> Invalidated, Action<string> Renamed);
