using System.Runtime.CompilerServices;
using Gnomon.Core;
using Microsoft.Win32;
using System.Windows.Interop;
using Windows.Media.Control;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Gnomon.Agent;

public sealed class ActivityProbes : IDisposable
{
    private volatile bool _locked;
    private volatile bool _displayAwake = true;
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private readonly HwndSource _messageWindow;

    public ActivityProbes()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _messageWindow = new HwndSource(new HwndSourceParameters("GnomonPowerMonitor")
        {
            Width = 0, Height = 0, WindowStyle = 0
        });
        _messageWindow.AddHook(PowerWindowProc);
    }

    public bool SessionActive => !_locked && _displayAwake && !IsScreenSaverRunning();

    public unsafe bool IsInputIdle(TimeSpan timeout)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Unsafe.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info)) return false;
        var idleMilliseconds = PInvoke.GetTickCount64() - info.dwTime;
        return idleMilliseconds > timeout.TotalMilliseconds;
    }

    public async Task<bool> IsMediaPlayingAsync(int foregroundPid)
    {
        try
        {
            _mediaManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_mediaManager.GetSessions().Any(session =>
                    session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing))
                return true;
        }
        catch { /* GSMTC can be unavailable in restricted sessions */ }

        // NAudio ships its CoreAudio session surface in the narrow NAudio.Wasapi
        // assembly (not the full NAudio bundle). Late binding keeps this probe
        // isolated and lets GSMTC continue if CoreAudio is unavailable.
        return NAudioSessionFallback.IsForegroundSessionActive(foregroundPid);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) _locked = true;
        if (e.Reason == SessionSwitchReason.SessionUnlock) _locked = false;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _displayAwake = false;
        if (e.Mode == PowerModes.Resume) _displayAwake = true;
    }

    private IntPtr PowerWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmSysCommand = 0x0112;
        const int ScMonitorPower = 0xF170;
        if (message == WmSysCommand && ((long)wParam & 0xFFF0) == ScMonitorPower)
        {
            _displayAwake = lParam.ToInt64() == -1;
            handled = false;
        }
        return IntPtr.Zero;
    }

    private static unsafe bool IsScreenSaverRunning()
    {
        int running = 0;
        return PInvoke.SystemParametersInfo(
            Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETSCREENSAVERRUNNING,
            0, &running, 0) && running != 0;
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _messageWindow.RemoveHook(PowerWindowProc);
        _messageWindow.Dispose();
    }
}

internal static class NAudioSessionFallback
{
    public static bool IsForegroundSessionActive(int foregroundPid)
    {
        try
        {
            var enumeratorType = Type.GetType("NAudio.CoreAudioApi.MMDeviceEnumerator, NAudio.Wasapi");
            if (enumeratorType is null) return false;
            using var enumerator = (IDisposable)Activator.CreateInstance(enumeratorType)!;
            var device = enumeratorType.GetMethod("GetDefaultAudioEndpoint")?.Invoke(enumerator,
                [Enum.Parse(Type.GetType("NAudio.CoreAudioApi.DataFlow, NAudio.Wasapi")!, "Render"),
                 Enum.Parse(Type.GetType("NAudio.CoreAudioApi.Role, NAudio.Wasapi")!, "Multimedia")]);
            if (device is null) return false;
            using var disposableDevice = device as IDisposable;
            var manager = device.GetType().GetProperty("AudioSessionManager")?.GetValue(device);
            var sessions = manager?.GetType().GetProperty("Sessions")?.GetValue(manager);
            if (sessions is null) return false;
            var count = (int)(sessions.GetType().GetProperty("Count")?.GetValue(sessions) ?? 0);
            for (var i = 0; i < count; i++)
            {
                var session = sessions.GetType().GetProperty("Item")?.GetValue(sessions, [i]);
                if (session is null) continue;
                var pid = (uint)(session.GetType().GetProperty("GetProcessID")?.GetValue(session) ?? 0u);
                var state = session.GetType().GetProperty("State")?.GetValue(session)?.ToString();
                if (pid == foregroundPid && state == "AudioSessionStateActive") return true;
            }
        }
        catch { }
        return false;
    }
}
