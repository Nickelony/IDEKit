// The Core suite is the one test project that runs its tests in parallel: a pure library suite with no window
// station and no whole-process test, so method-level parallelism with a small worker count shortens the run.
//
// The worker count is bounded rather than "all processors": several tests are timing probes or concurrency
// harnesses whose value depends on the scheduler having room left. The [DoNotParallelize] markers that pin the
// process-global probes take effect only because this file enables parallelism.
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]
