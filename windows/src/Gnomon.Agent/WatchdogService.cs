using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Gnomon.Core;
using Microsoft.Win32.SafeHandles;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Gnomon.Agent;

public sealed class WatchdogService
{
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { EnsureWorker(); }
            catch (Exception ex) { Log.Error(ex, "Could not ensure session worker"); }
            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static void EnsureWorker()
    {
        var sessionId = (int)PInvoke.WTSGetActiveConsoleSessionId();
        if (sessionId < 0 || sessionId == -1) return;
        var configuredUser = LoadConfiguredUser();
        if (string.IsNullOrWhiteSpace(configuredUser) || !SessionBelongsToUser((uint)sessionId, configuredUser)) return;
        var executable = Program.ExecutablePath;
        if (Process.GetProcessesByName("Gnomon.Agent").Any(x =>
        {
            try
            {
                return x.SessionId == sessionId &&
                    string.Equals(
                        Path.GetFullPath(x.MainModule?.FileName ?? ""),
                        Path.GetFullPath(executable),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        })) return;
        SessionLauncher.Launch(executable, (uint)sessionId);
        Log.Information("Relaunched session worker in session {SessionId}", sessionId);
    }

    private static string LoadConfiguredUser()
    {
        try
        {
            var json = File.ReadAllText(AgentPaths.Create().ConfigFile);
            return JsonSerializer.Deserialize<AgentConfig>(json, ProtocolCodec.JsonOptions)?.WindowsUser ?? "";
        }
        catch (Exception ex) { Log.Warning(ex, "Watchdog could not read configuration"); return ""; }
    }

    private static unsafe bool SessionBelongsToUser(uint sessionId, string configuredUser)
    {
        HANDLE token = default;
        if (!PInvoke.WTSQueryUserToken(sessionId, ref token)) return false;
        try
        {
            using var identity = new WindowsIdentity((nint)token.Value);
            var actual = identity.Name.Split('\\').Last();
            return actual.Equals(configuredUser, StringComparison.OrdinalIgnoreCase);
        }
        finally { PInvoke.CloseHandle(token); }
    }
}

internal static class SessionLauncher
{
    public static unsafe void Launch(string executable, uint sessionId)
    {
        HANDLE userToken = default;
        if (!PInvoke.WTSQueryUserToken(sessionId, ref userToken))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            using var userTokenHandle = new SafeFileHandle((nint)userToken.Value, ownsHandle: false);
            if (!PInvoke.DuplicateTokenEx(userTokenHandle, TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS, null,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out var primary))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            using (primary)
            {
                void* environment;
                if (!PInvoke.CreateEnvironmentBlock(out environment, primary, false))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    var commandLine = $"\"{executable}\"";
                    // CsWin32's mutable command-line overload requires the caller to
                    // include the terminating NUL in the backing buffer.
                    var commandBuffer = (commandLine + '\0').ToCharArray();
                    Span<char> mutableCommandLine = commandBuffer;
                    var desktop = "winsta0\\default";
                    fixed (char* desktopPtr = desktop)
                    {
                        var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW), lpDesktop = desktopPtr };
                        if (!PInvoke.CreateProcessAsUser(primary, null, ref mutableCommandLine, null, null, false,
                                PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT,
                                environment, null, in startup, out var processInfo))
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                        PInvoke.CloseHandle(processInfo.hThread);
                        PInvoke.CloseHandle(processInfo.hProcess);
                    }
                }
                finally { if (environment != null) PInvoke.DestroyEnvironmentBlock(environment); }
            }
        }
        finally { PInvoke.CloseHandle(userToken); }
    }
}
