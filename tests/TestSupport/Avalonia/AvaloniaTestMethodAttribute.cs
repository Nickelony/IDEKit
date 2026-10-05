using System.Runtime.CompilerServices;

namespace Nickelony.Testing;

/// <summary>Dispatches one test method's body onto the Avalonia UI thread, so a test can construct and lay out controls there.</summary>
/// <remarks>
/// Delegates execution to the supplied attribute while the session pumps its dispatcher.
/// </remarks>
internal sealed class AvaloniaTestMethodAttribute : TestMethodAttribute
{
	private readonly TestMethodAttribute _inner;

	/// <param name="inner">The attribute the runtime supplied for the test method.</param>
	public AvaloniaTestMethodAttribute(
		TestMethodAttribute inner,
		[CallerFilePath] string callerFilePath = "",
		[CallerLineNumber] int callerLineNumber = -1
	)
		: base(callerFilePath, callerLineNumber) => _inner = inner;

	/// <inheritdoc/>
	public override Task<TestResult[]> ExecuteAsync(ITestMethod testMethod) =>
		AvaloniaToolkitTestHost.RunAsync(() => _inner.ExecuteAsync(testMethod));
}
