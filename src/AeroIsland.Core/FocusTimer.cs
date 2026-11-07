namespace AeroIsland.Core;

public enum FocusTimerPhase { Idle, Running, Paused }

public sealed class FocusTimer
{
    private DateTimeOffset _endUtc;
    private TimeSpan _pausedRemaining;

    public FocusTimerPhase Phase { get; private set; }
    public TimeSpan Duration { get; private set; }

    public void Start(TimeSpan duration, DateTimeOffset now)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(4))
            throw new ArgumentOutOfRangeException(nameof(duration));
        Duration = duration;
        _pausedRemaining = duration;
        _endUtc = now + duration;
        Phase = FocusTimerPhase.Running;
    }

    public TimeSpan Remaining(DateTimeOffset now) => Phase switch
    {
        FocusTimerPhase.Running => _endUtc > now ? _endUtc - now : TimeSpan.Zero,
        FocusTimerPhase.Paused => _pausedRemaining,
        _ => TimeSpan.Zero
    };

    public double Progress(DateTimeOffset now) => Duration == TimeSpan.Zero ? 0 :
        Math.Clamp(1 - Remaining(now).TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);

    public void Pause(DateTimeOffset now)
    {
        if (Phase != FocusTimerPhase.Running) return;
        _pausedRemaining = Remaining(now);
        Phase = FocusTimerPhase.Paused;
    }

    public void Resume(DateTimeOffset now)
    {
        if (Phase != FocusTimerPhase.Paused) return;
        _endUtc = now + _pausedRemaining;
        Phase = FocusTimerPhase.Running;
    }

    public bool CompleteIfElapsed(DateTimeOffset now)
    {
        if (Phase != FocusTimerPhase.Running || _endUtc > now) return false;
        Reset();
        return true;
    }

    public void Reset()
    {
        Phase = FocusTimerPhase.Idle;
        Duration = TimeSpan.Zero;
        _pausedRemaining = TimeSpan.Zero;
        _endUtc = default;
    }
}
