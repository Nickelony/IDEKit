using System.Runtime.CompilerServices;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Dispatches one test method's body onto the Avalonia UI thread, so the mirror suites can construct and
/// lay out editor controls the way the WPF suites do on their STA thread.
/// </summary>
/// <remarks>
/// The wrapper is installed by <see cref="AvaloniaTestClassAttribute"/>; a test method never uses it
/// directly. It delegates the real execution to the attribute the runtime supplied for the method and
/// awaits that work while the headless session pumps its dispatcher loop, so both synchronous and
/// asynchronous test bodies keep their continuations on the UI thread.
/// </remarks>
internal sealed class AvaloniaTestMethodAttribute : TestMethodAttribute
{
	private readonly TestMethodAttribute _inner;

	/// <summary>
	/// Initializes a new instance of the <see cref="AvaloniaTestMethodAttribute"/> class.
	/// </summary>
	/// <param name="inner">The attribute the runtime supplied for the test method.</param>
	/// <param name="callerFilePath">The caller's source file, supplied by the compiler.</param>
	/// <param name="callerLineNumber">The caller's source line, supplied by the compiler.</param>
	public AvaloniaTestMethodAttribute(
		TestMethodAttribute inner,
		[CallerFilePath] string callerFilePath = "",
		[CallerLineNumber] int callerLineNumber = -1)
		: base(callerFilePath, callerLineNumber)
		=> _inner = inner;

	/// <inheritdoc/>
	public override Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
		=> AvaloniaTestHost.RunAsync(() => _inner.ExecuteAsync(testMethod));
}
