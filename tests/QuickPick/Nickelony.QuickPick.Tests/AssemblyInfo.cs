// The quick-pick suite runs its tests in parallel: a pure library suite with no window station, whose controller tests each own a pumped UI
// thread, so method-level parallelism with a small worker count shortens the run.
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]
