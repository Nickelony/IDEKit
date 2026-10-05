using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.Core.LineStatus;

/// <summary>Provides the document lines a line-status consumer marks.</summary>
/// <remarks>
/// The numbers must refer to the current document; a consumer walks them forward once per render
/// pass and ignores those outside it. <see cref="GetMarkedLineNumbers"/> is called every render pass, so it must not throw: return an empty
/// list rather than <see langword="null"/>, and cache the result. A source that also raises <see cref="IChangeNotificationSource.Changed"/> spares
/// the host a manual invalidation.
/// </remarks>
public interface ILineStatusSource
{
	/// <summary>Returns the one-based document line numbers to mark, sorted ascending.</summary>
	IReadOnlyList<int> GetMarkedLineNumbers();
}
