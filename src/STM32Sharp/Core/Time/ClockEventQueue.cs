namespace STM32.Core.Time;

/// <summary>A scheduled callback returned by <see cref="ClockEventQueue.Schedule"/>; used to cancel it.</summary>
public sealed class ClockEvent
{
    internal long AtCycle;
    internal Action Callback;
    internal ClockEvent? Next;
    internal bool Cancelled;

    internal ClockEvent(long atCycle, Action callback)
    {
        AtCycle = atCycle;
        Callback = callback;
    }
}

/// <summary>
/// A cycle-accurate event scheduler, mirroring the clock-event model of avr8js / rp2040js. Callbacks
/// are queued against an absolute cycle count (the CPU's <c>Cycles</c>) and fired, in order, the moment
/// the clock reaches them. It lets an external simulator ("solver") co-simulate at cycle granularity:
/// schedule a pin change, a bus event or a timeout at an exact cycle, ask for the next event cycle to
/// know how far it may advance, and have peripherals deliver interrupts at the right instant rather
/// than at the end of an arbitrary run batch.
///
/// Backed by a singly-linked list kept sorted by <see cref="ClockEvent.AtCycle"/> — the same structure
/// avr8js uses — which is O(n) to insert but optimal for the handful of live events typical in an MCU.
/// </summary>
public sealed class ClockEventQueue
{
    private ClockEvent? _head;

    /// <summary>Absolute cycle of the earliest pending event, or null when the queue is empty.</summary>
    public long? NextCycle => _head?.AtCycle;

    /// <summary>True when no events are pending.</summary>
    public bool IsEmpty => _head is null;

    /// <summary>Schedule <paramref name="callback"/> to fire when the clock reaches <paramref name="atCycle"/>.</summary>
    public ClockEvent Schedule(long atCycle, Action callback)
    {
        var ev = new ClockEvent(atCycle, callback);
        Insert(ev);
        return ev;
    }

    private void Insert(ClockEvent ev)
    {
        if (_head is null || ev.AtCycle < _head.AtCycle)
        {
            ev.Next = _head;
            _head = ev;
            return;
        }
        var cur = _head;
        while (cur.Next is not null && cur.Next.AtCycle <= ev.AtCycle)
            cur = cur.Next;
        ev.Next = cur.Next;
        cur.Next = ev;
    }

    /// <summary>Cancel a previously scheduled event. Safe to call more than once.</summary>
    public void Cancel(ClockEvent ev)
    {
        ev.Cancelled = true;
        if (_head is null) return;
        if (ReferenceEquals(_head, ev)) { _head = _head.Next; return; }
        var cur = _head;
        while (cur.Next is not null && !ReferenceEquals(cur.Next, ev))
            cur = cur.Next;
        if (cur.Next is not null) cur.Next = cur.Next.Next;
    }

    /// <summary>Fire every event whose cycle is at or before <paramref name="now"/>, earliest first.</summary>
    public void RunDue(long now)
    {
        while (_head is not null && _head.AtCycle <= now)
        {
            var ev = _head;
            _head = _head.Next;
            if (!ev.Cancelled) ev.Callback();
        }
    }

    /// <summary>Remove all pending events.</summary>
    public void Clear() => _head = null;
}
