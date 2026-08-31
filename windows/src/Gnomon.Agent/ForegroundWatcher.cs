using System.Diagnostics;
using Gnomon.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Gnomon.Agent;

public sealed class ForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private readonly WINEVENTPROC _callback;
    private HWINEVENTHOOK _hook;
    public event EventHandler<ForegroundChangedEventArgs>? Changed;
    public ForegroundChangedEventArgs Current { get; private set; } = new(0, "", "");

    public ForegroundWatcher()
    {
        _callback = OnWinEvent;
        _hook = PInvoke.SetWinEventHook(
            EventSystemForeground, EventSystemForeground,
            HMODULE.Null, _callback, 0, 0, WinEventOutOfContext);
        Update(PInvoke.GetForegroundWindow());
    }

    private void OnWinEvent(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int objectId, int childId,
        uint eventThread, uint eventTime) => Update(hwnd);

    private unsafe void Update(HWND hwnd)
    {
        if (hwnd.IsNull) return;
        uint pid = 0;
        PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = (process.ProcessName + ".exe").ToLowerInvariant();
            var hint = "";
            try
            {
                var info = process.MainModule?.FileVersionInfo;
                hint = string.Join(" by ", new[] { info?.FileDescription, info?.CompanyName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
            catch { /* protected processes deliberately have no classification fallback */ }
            Current = new((int)pid, name, hint);
        }
        catch
        {
            Current = new((int)pid, "unknown.exe", "Protected process");
        }
        Changed?.Invoke(this, Current);
    }

    public void Dispose()
    {
        if (!_hook.IsNull) PInvoke.UnhookWinEvent(_hook);
        _hook = default;
    }
}

public sealed class ForegroundChangedEventArgs(
    int processId,
    string processName,
    string hint
) : EventArgs
{
    public int ProcessId { get; } = processId;
    public string ProcessName { get; } = processName;
    public string Hint { get; } = hint;
}
