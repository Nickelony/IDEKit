namespace Nickelony.Testing;

/// <summary>
/// Provides the <see cref="TestCategoryAttribute"/> names for selecting opt-in or environment-dependent test tiers, so the strings exist once in code
/// and the run settings under <c>tests</c>, the filters in the test project files and the command line select a stable set.
/// </summary>
internal static class TestCategories
{
	/// <summary>
	/// Tests that assert allocation or wall-clock budgets. They are informative smoke checks, not deterministic assertions, so a run can deselect
	/// them with TestCategory!=Performance instead of loosening a bound.
	/// </summary>
	public const string Performance = "Performance";

	/// <summary>
	/// Tests that need a real external dependency (a child process, a file watcher, or a developer-supplied language server). They only provide
	/// protection on a machine where that dependency is present, so a fast or hermetic run can deselect them.
	/// </summary>
	public const string Integration = "Integration";

	/// <summary>
	/// Tests that show real WPF content in a window and therefore need an interactive window station. On a session without one the shared
	/// <c>WpfTestHost</c> reports them inconclusive instead of failing, so a headless run silently drops their coverage.
	/// </summary>
	public const string InteractiveWindow = "InteractiveWindow";

	/// <summary>
	/// Tests that assert a repository-wide rule over the tree rather than a product behavior, so their result describes the checkout.
	/// </summary>
	public const string Repository = "Repository";

	/// <summary>
	/// Tests that print a measure of the repository for a reviewer to read and never fail, so the guards in <see cref="Repository"/> stay
	/// a pass-or-fail set.
	/// </summary>
	public const string RepositoryReport = "RepositoryReport";
}
