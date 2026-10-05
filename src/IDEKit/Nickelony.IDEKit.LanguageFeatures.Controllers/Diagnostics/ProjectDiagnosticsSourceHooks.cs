using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;

/// <summary>Groups the required host hook used by the <see cref="ProjectDiagnosticsSource"/>.</summary>
/// <param name="AnalyzeAsync">
/// Analyzes the whole project and returns the diagnostics of each document, keyed by document id. A document that the result omits,
/// or lists with no diagnostics, has none. The source cancels its token when a later change supersedes the run or the source is disposed.
/// </param>
/// <remarks>
/// <para>
/// The source calls the hook on the dispatcher thread, on the turn that starts the run, and does not move the call off the dispatcher.
/// The hook captures synchronously the text it needs, such as the text of the editors, which an editor allows only on its own thread,
/// and then offloads the work itself. The source knows no paths and no buffers.
/// </para>
/// <para>
/// The source does not call the hook again until the task of the earlier call completes, even when it canceled that call, so a hook
/// that ignores its token never runs twice at once and needs no reentrancy. A result that is <see langword="null"/>, a <see langword="null"/> list
/// or entry in it, a <see langword="null"/> task, a thrown exception, and an <see cref="OperationCanceledException"/> without a canceled token
/// all make a failed analysis. A result for document ids that the comparer of the options treats as equal adds up under the first id.
/// </para>
/// <para>
/// The source rejects a <see langword="null"/> hook with an <see cref="ArgumentNullException"/>.
/// </para>
/// </remarks>
public readonly record struct ProjectDiagnosticsSourceHooks(
	Func<CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>>> AnalyzeAsync
);
