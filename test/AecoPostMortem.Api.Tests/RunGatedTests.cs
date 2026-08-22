using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// The write gate <c>ApiHost.IngestRoute</c> and <c>ApiHost.RebuildRoute</c> are both served behind
/// (<c>ApiHost.RunGated</c>, <c>internal</c>, reached here via <c>InternalsVisibleTo</c>). Exercised
/// directly against a manually-held <see cref="SemaphoreSlim"/> rather than by racing two real HTTP
/// requests against each other — a true race between two sub-millisecond in-memory operations is not
/// reliably reproducible over a real socket, so this proves the gate's own logic deterministically
/// instead (Scenario 2 of the brief: "must not let the operator fire the same command twice
/// concurrently").
/// </summary>
public sealed class RunGatedTests
{
    [Fact]
    public void An_already_held_gate_is_reported_as_a_conflict_without_running_the_work()
    {
        using var gate = new SemaphoreSlim(1, 1);
        gate.Wait(TestContext.Current.CancellationToken);

        var ran = false;
        var result = ApiHost.RunGated(gate, () =>
        {
            ran = true;
            return "unused";
        });

        Assert.False(ran);
        var conflict = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public void A_successful_run_releases_the_gate_so_a_later_call_can_acquire_it()
    {
        using var gate = new SemaphoreSlim(1, 1);

        var first = ApiHost.RunGated(gate, () => "first");
        var second = ApiHost.RunGated(gate, () => "second");

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(first);
        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)first).StatusCode);
        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)second)!.StatusCode);
        Assert.Equal(1, gate.CurrentCount);
    }

    /// <summary>A failed run must not leave the gate held forever — the finally-release has to run
    /// on the throwing path too, or one failed ingest would permanently lock out every future one
    /// without restarting the host.</summary>
    [Fact]
    public void A_failed_run_still_releases_the_gate_and_reports_the_failure()
    {
        using var gate = new SemaphoreSlim(1, 1);

        var result = ApiHost.RunGated<string>(gate, () => throw new InvalidOperationException("boom"));

        var problem = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.Equal(1, gate.CurrentCount);
    }
}
