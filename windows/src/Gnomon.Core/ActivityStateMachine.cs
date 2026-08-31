namespace Gnomon.Core;

public static class ActivityStateMachine
{
    public static bool IsCounting(ActivitySnapshot state) =>
        state.ForegroundMapped
        && state.SessionActive
        && (!state.InputIdle || (state.MediaPlaying && state.MediaCountsAsActive));
}
