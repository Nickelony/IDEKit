// The Core suite is the one test project that runs its tests in parallel. It is a pure library suite:
// no window station, no test that claims a whole process, and roughly a thousand mostly short tests,
// so method-level parallelism with a small worker count shortens the run substantially.
//
// The worker count is bounded rather than "all processors" on purpose. Several tests are timing probes
// or concurrency harnesses whose value depends on the scheduler having room left, and the suite marks
// the tests that must not overlap - [DoNotParallelize] on the process-global RegexCache bound probes
// and the concurrency probes, the culture-swapping sidecar test, the Turkish-culture find test and the
// process-global theme-registration tests. Those markers are inert until an assembly-level Parallelize
// attribute exists, which is what this file adds; the parallel run is verified as part of the phase
// that added it.
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]
