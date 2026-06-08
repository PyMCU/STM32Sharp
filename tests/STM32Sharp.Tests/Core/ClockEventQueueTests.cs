using STM32.Core.Time;

namespace STM32Sharp.Tests.Core;

public class ClockEventQueueTests
{
    [Fact]
    public void Fires_due_events_in_cycle_order()
    {
        var q = new ClockEventQueue();
        var log = new List<long>();
        q.Schedule(100, () => log.Add(100));
        q.Schedule(50, () => log.Add(50));
        q.Schedule(150, () => log.Add(150));

        q.NextCycle.Should().Be(50);
        q.RunDue(120); // 50 and 100 are due; 150 is not

        log.Should().Equal(50L, 100L);
        q.NextCycle.Should().Be(150);
    }

    [Fact]
    public void Cancel_prevents_an_event_from_firing()
    {
        var q = new ClockEventQueue();
        var fired = false;
        var ev = q.Schedule(10, () => fired = true);
        q.Cancel(ev);

        q.RunDue(100);
        fired.Should().BeFalse();
        q.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Events_at_the_same_cycle_keep_insertion_order()
    {
        var q = new ClockEventQueue();
        var log = new List<string>();
        q.Schedule(10, () => log.Add("a"));
        q.Schedule(10, () => log.Add("b"));
        q.RunDue(10);
        log.Should().Equal("a", "b");
    }
}
