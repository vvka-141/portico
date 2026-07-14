using Xunit;

// Test classes run SERIALLY in this assembly. This is not a performance oversight — it is
// required for correctness, and the suite runs in ~2 seconds regardless.
//
// The console is process-global. Two things in this suite touch it in incompatible ways:
//
//   - CliTestHarness swaps Console.Out / Console.Error to capture a run's output. Its internal
//     semaphore serializes harness-vs-harness runs, but cannot serialize it against anything
//     else in the process.
//   - Tests that exercise the real ICliConsole (e.g. CliApplicationRobustness_Should, which
//     calls app.Run(...) with no injected console) write to the real Console.Error.
//
// Run those two in parallel classes and the harness captures the other test's stderr, so an
// assertion like `Assert.Equal(string.Empty, result.StandardError.Trim())` fails intermittently.
// This is a latent race inherited from the origin suite; adding the analyzer test classes
// changed xUnit's scheduling enough to surface it. The origin's own note on CliTestHarness
// prescribes exactly this remedy.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
