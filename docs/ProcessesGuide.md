# Processes guide

`Nickelony.IDEKit.Processes` runs an external process to completion for compiler, formatter, linter, and
generator integration. This guide owns the batch-scoped design, the output-capture behavior, the request
validation, the outcome model, the handle lifetime, the options, and the cancellation contract.

The package depends on nothing - not even the other `Nickelony.IDEKit` packages - so a host adopts process
execution without an editor or UI-framework dependency.

## Example

```csharp
IProcessRunner runner = new ProcessRunner();

ProcessRunResult result = await runner.RunAsync(
    new ProcessRunRequest
    {
        FileName = "dotnet",
        ArgumentList = ["--version"],
        RedirectStandardOutput = true,
        Timeout = TimeSpan.FromSeconds(10)
    },
    cancellationToken);

if (!result.Succeeded)
    Console.WriteLine($"{result.Outcome}, exit code {result.ExitCode}");
else if (result.StandardOutputTruncated)
    Console.WriteLine("The output was cut at the capture bound.");
else
    Console.WriteLine(result.StandardOutput);
```

`Succeeded` is `true` only for an `Exited` outcome with exit code 0, so a timeout, a cancellation, and a failing
exit code all take the first branch. [`SampleProcessRunner`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Processes/SampleProcessRunner.cs) runs the
command line of the sample settings through the runner.

## Batch-scoped by design

One request runs one process to a terminal outcome, and redirected standard output and error are drained while
it runs, so output larger than the operating-system pipe buffer cannot block the run. There is no standard-input
piping or streaming; an interactive or streaming process is out of scope.

## Output capture

Capture is bounded and can fail. After the exit is observed, the capture waits a bounded grace period for the
pipes to close. It reports the text as unavailable when a descendant keeps them open or the capture fails,
including a configured encoding that rejects a captured byte sequence.

A byte-order mark at the start of a redirected stream is not part of the captured text. The reader that the
runtime attaches to the pipe consumes the mark and decodes with the encoding the mark names, so a UTF-8 or
UTF-16 mark wins over the configured `StandardOutputEncoding` or `StandardErrorEncoding`, and the text never
starts with U+FEFF.

A configured capture bound is applied while the process runs: the excess is discarded, the pipe keeps being
drained, and a truncation flag reports that the text may be incomplete.

Each redirected stream is drained until the child closes it, so a quiet child keeps its drain pending while the process runs.
The runtime's redirected pipes are synchronous, so each pending drain parks one thread-pool thread for as long
as the child is quiet. A host that runs many quiet children at once pays for those threads.

## Request validation

The runner throws `ArgumentException` for these combinations before it launches anything:

- shell execution with redirection or environment overrides,
- both argument shapes set (`RawArguments` and `ArgumentList`),
- an encoding without the matching redirection.

## Outcomes

A run reports how it ended as one `ProcessRunOutcome` value on its `ProcessRunResult`, not as correlated flags:

| Outcome | Meaning |
| --- | --- |
| `NoProcessHandle` | No process was produced to wait for. Shell execution can start the target without returning a handle. |
| `CanceledBeforeStart` | The token was already canceled when the run was requested, or was canceled by the time a launch that returned no handle completed. |
| `Exited` | The process exited and the exit was observed. |
| `TimedOut` | The timeout ended the wait, and termination was attempted. |
| `Canceled` | Cancellation ended the wait, and termination was attempted. |

`ExitCode` is `null` when the exit was not observed.

`Run` and `RunAsync` dispose the process handle before they return, and disposal does not terminate the process.
A `TimedOut` or `Canceled` run whose termination was not confirmed within the termination grace period can
therefore leave the process running. Such a result reports its `ProcessId` and carries no exit code and no
captured output.

## Driving the lifetime

`IProcessLauncher.Start` returns an `IProcessHandle` for a caller that drives the process itself. It throws
`ArgumentException` for shell execution, because a shell launch produces no process handle, and disposing the handle
does not terminate the process. `ProcessRunner` implements `IProcessLauncher` beside `IProcessRunner`, so a consumer
that only waits for an outcome types against `IProcessRunner` and its fake needs no handle.

## Options

`ProcessRunnerOptions` holds the termination-confirmation and output-drain grace periods and the per-stream
capture bound, and the type documents their defaults and the cost of a shorter period.

## Cancellation

Cancellation is an outcome, not an exception. An already-canceled token yields `CanceledBeforeStart` without
launching anything; a token canceled while the runner waits yields `Canceled` after the runner attempted to
terminate the process and its descendants. After a timeout or cancellation, termination confirmation and output
capture can add the configured grace periods before the call returns.
