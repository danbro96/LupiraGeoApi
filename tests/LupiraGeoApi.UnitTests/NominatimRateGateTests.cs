using LupiraGeoApi.Application;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LupiraGeoApi.UnitTests;

public sealed class NominatimRateGateTests
{
    [Fact]
    public async Task First_caller_passes_immediately()
    {
        var gate = new NominatimRateGate(new FakeTimeProvider());
        var first = gate.WaitTurnAsync();
        Assert.True(first.IsCompletedSuccessfully);
        await first;
    }

    [Fact]
    public async Task Second_caller_waits_out_the_min_interval()
    {
        var time = new FakeTimeProvider();
        var gate = new NominatimRateGate(time);
        await gate.WaitTurnAsync();

        var second = gate.WaitTurnAsync();
        Assert.False(second.IsCompleted);

        time.Advance(NominatimRateGate.MinInterval);
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Advancing_less_than_the_interval_keeps_the_caller_queued()
    {
        var time = new FakeTimeProvider();
        var gate = new NominatimRateGate(time);
        await gate.WaitTurnAsync();

        var second = gate.WaitTurnAsync();
        time.Advance(NominatimRateGate.MinInterval / 2);
        Assert.False(second.IsCompleted);

        time.Advance(NominatimRateGate.MinInterval);
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
