using System.Collections.Concurrent;

namespace Camunda.Startup.DemoApp.Feature;

public sealed class LoadTestRegistry
{
    private readonly ConcurrentDictionary<string, LoadTestRun> runs = new();

    public void MarkStarted(string loadTestId, string instanceId) =>
        runs.GetOrAdd(loadTestId, static _ => new()).Started.TryAdd(instanceId, 0);

    public void MarkCompleted(string loadTestId, string instanceId) =>
        runs.GetOrAdd(loadTestId, static _ => new()).Completed.TryAdd(instanceId, 0);

    public LoadTestRunStatus GetStatus(string loadTestId)
    {
        var run = runs.GetOrAdd(loadTestId, static _ => new());
        return new LoadTestRunStatus(loadTestId, run.Started.Count, run.Completed.Count);
    }

    private sealed class LoadTestRun
    {
        public ConcurrentDictionary<string, byte> Started { get; } = new();
        public ConcurrentDictionary<string, byte> Completed { get; } = new();
    }
}

public sealed record LoadTestRunStatus(string LoadTestId, int Started, int Completed);
