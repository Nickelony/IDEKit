using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>A pointer source that answers with a script the test sets, and counts its reads and its disposals.</summary>
/// <remarks>
/// The default script reports the constant offset <c>5</c>, so a test that does not care about the pointer needs no setup. A test replaces
/// <see cref="Offset"/> to report no offset, to change the answer between the request and the publication, or to fail. Like a real pointer source
/// it accepts a read only on the thread of its dispatcher, so a controller that reads it elsewhere fails the test.
/// </remarks>
internal sealed class ScriptedHoverPointerSource(IUiDispatcher dispatcher) : IHoverPointerSource
{
	/// <summary>Holds the answer to a read, which defaults to the constant offset <c>5</c>; <see langword="null"/> means no offset.</summary>
	public Func<int?> Offset { get; set; } = static () => 5;

	/// <summary>The number of times the controller asked for the offset.</summary>
	public int ReadCount { get; private set; }

	/// <summary>The number of times the controller disposed the source.</summary>
	public int DisposeCount { get; private set; }

	/// <inheritdoc/>
	public int? GetHoveredOffset()
	{
		dispatcher.VerifyAccess();
		ReadCount++;

		return Offset();
	}

	/// <inheritdoc/>
	public void Dispose() => DisposeCount++;
}
