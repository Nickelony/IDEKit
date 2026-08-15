namespace Nickelony.Testing;

/// <summary>
/// Provides the <see cref="TestCategoryAttribute"/> names used to select opt-in or environment-dependent
/// test tiers, so the strings exist once and the release gate can filter on a stable set.
/// </summary>
internal static class TestCategories
{
	/// <summary>
	/// Tests that assert allocation or wall-clock budgets. They are informative smoke checks, not
	/// deterministic assertions, so a variance-flagged run can deselect them instead of loosening a bound.
	/// </summary>
	public const string Performance = "Performance";

	/// <summary>
	/// Tests that need a real external dependency (a child process, a file watcher, a language-server
	/// binary, or a downloaded asset). They only provide protection on a machine where that dependency
	/// is present, so a fast or hermetic run can deselect them.
	/// </summary>
	public const string Integration = "Integration";

	/// <summary>
	/// Tests that show real WPF content in a window and therefore need an interactive window station. On a
	/// session without one the shared <c>WPFTestHost</c> reports them inconclusive instead of failing, so a
	/// headless run silently drops their coverage. The name is canonical for the deferred CI leg that will
	/// apply it per test and deselect the tier explicitly.
	/// </summary>
	public const string InteractiveWindow = "InteractiveWindow";
}
