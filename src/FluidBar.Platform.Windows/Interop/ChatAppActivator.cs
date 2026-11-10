using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FluidBar.Platform.Windows.Interop;

/// <summary>
/// Supported social chat platforms for notification styling and window jumping.
/// </summary>
public enum SocialPlatform
{
    /// <summary>微信 (WeChat)</summary>
    WeChat,

    /// <summary>钉钉 (DingTalk)</summary>
    DingTalk,

    /// <summary>飞书 (Feishu / Lark)</summary>
    Feishu,

    /// <summary>企业微信 (WeCom / WXWork)</summary>
    WeCom
}

/// <summary>
/// Status outcome of attempting to activate or jump to a chat app.
/// </summary>
public enum ChatActivationStatus
{
    /// <summary>Existing window found and brought to top.</summary>
    FoundAndFocused,

    /// <summary>Process was not running or no window; launched executable/URI.</summary>
    LaunchedProcess,

    /// <summary>Application could not be found or launched.</summary>
    NotFound,

    /// <summary>Error during activation.</summary>
    Error
}

/// <summary>
/// Result details of a chat app jump attempt.
/// </summary>
public sealed record ChatActivationResult(
    SocialPlatform Platform,
    ChatActivationStatus Status,
    string Message,
    IntPtr Hwnd = default);

/// <summary>
/// Windows API based activator that locates, restores, and brings to top chat applications (WeChat, DingTalk, Feishu, WeCom),
/// or falls back to launching the application with friendly guidance.
/// </summary>
public static class ChatAppActivator
{
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Gets display name for the social platform.
    /// </summary>
    public static string GetDisplayName(SocialPlatform platform) => platform switch
    {
        SocialPlatform.WeChat => "微信",
        SocialPlatform.DingTalk => "钉钉",
        SocialPlatform.Feishu => "飞书",
        SocialPlatform.WeCom => "企业微信",
        _ => platform.ToString()
    };

    /// <summary>
    /// Attempts to bring the chat application window to the foreground, or launches it if not running.
    /// </summary>
    public static ChatActivationResult Activate(SocialPlatform platform)
    {
        var appName = GetDisplayName(platform);
        try
        {
            var targetHwnd = FindAppWindow(platform);
            if (targetHwnd != IntPtr.Zero)
            {
                var broughtToFront = ForceForegroundWindow(targetHwnd);
                return new ChatActivationResult(
                    platform,
                    ChatActivationStatus.FoundAndFocused,
                    broughtToFront ? $"已将【{appName}】置顶到前台" : $"已尝试激活【{appName}】窗口",
                    targetHwnd);
            }

            // Window not found, try to launch application
            if (TryLaunchApp(platform, out var launchMsg))
            {
                return new ChatActivationResult(
                    platform,
                    ChatActivationStatus.LaunchedProcess,
                    $"未检测到【{appName}】运行，已为您启动主界面");
            }

            return new ChatActivationResult(
                platform,
                ChatActivationStatus.NotFound,
                $"未检测到【{appName}】正在运行，且未找到安装程序，请确认已安装该应用");
        }
        catch (Exception ex)
        {
            return new ChatActivationResult(
                platform,
                ChatActivationStatus.Error,
                $"激活【{appName}】时发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// Forces the target window to the foreground using AttachThreadInput technique to bypass OS foreground lock.
    /// </summary>
    public static bool ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
        }
        else
        {
            ShowWindow(hWnd, SW_SHOW);
        }

        var fgHwnd = GetForegroundWindow();
        var curThreadId = GetCurrentThreadId();
        var fgThreadId = GetWindowThreadProcessId(fgHwnd, out _);
        var targetThreadId = GetWindowThreadProcessId(hWnd, out _);

        var attachedFg = false;
        var attachedTarget = false;

        if (fgThreadId != 0 && fgThreadId != curThreadId)
        {
            attachedFg = AttachThreadInput(curThreadId, fgThreadId, true);
        }
        if (targetThreadId != 0 && targetThreadId != curThreadId)
        {
            attachedTarget = AttachThreadInput(curThreadId, targetThreadId, true);
        }

        BringWindowToTop(hWnd);
        var success = SetForegroundWindow(hWnd);

        if (attachedFg)
        {
            AttachThreadInput(curThreadId, fgThreadId, false);
        }
        if (attachedTarget)
        {
            AttachThreadInput(curThreadId, targetThreadId, false);
        }

        return success;
    }

    /// <summary>
    /// Searches for the top-level window of the specified chat platform.
    /// </summary>
    public static IntPtr FindAppWindow(SocialPlatform platform)
    {
        var foundHwnds = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                // Also check iconic windows
                if (!IsIconic(hWnd))
                {
                    return true;
                }
            }

            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0) return true;

            string? processName = null;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch
            {
                return true;
            }

            var titleSb = new StringBuilder(256);
            GetWindowText(hWnd, titleSb, 256);
            var title = titleSb.ToString();

            var classSb = new StringBuilder(256);
            GetClassName(hWnd, classSb, 256);
            var className = classSb.ToString();

            if (IsMatchingWindow(platform, processName, className, title))
            {
                foundHwnds.Add(hWnd);
                return false; // Stop enumeration
            }

            return true;
        }, IntPtr.Zero);

        return foundHwnds.Count > 0 ? foundHwnds[0] : IntPtr.Zero;
    }

    private static bool IsMatchingWindow(SocialPlatform platform, string processName, string className, string title)
    {
        switch (platform)
        {
            case SocialPlatform.WeChat:
                if (processName.Equals("WeChat", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("WeChatAppEx", StringComparison.OrdinalIgnoreCase))
                {
                    if (className.Contains("WeChatMainWndForPC", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("ChatWnd", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("微信", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (className.Contains("WeChat", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(title))
                    {
                        return true;
                    }
                }
                break;

            case SocialPlatform.DingTalk:
                if (processName.Equals("DingTalk", StringComparison.OrdinalIgnoreCase))
                {
                    if (className.Contains("DingTalk", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("钉钉", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("DingTalk", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return true;
                    }
                }
                break;

            case SocialPlatform.Feishu:
                if (processName.Equals("Feishu", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("Lark", StringComparison.OrdinalIgnoreCase))
                {
                    if (title.Contains("飞书", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("Feishu", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("Lark", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            return true;
                        }
                    }
                }
                break;

            case SocialPlatform.WeCom:
                if (processName.Equals("WXWork", StringComparison.OrdinalIgnoreCase))
                {
                    if (className.Contains("WXWorkMainWnd", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("企业微信", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("WXWork", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return true;
                    }
                }
                break;
        }

        return false;
    }

    private static bool TryLaunchApp(SocialPlatform platform, out string? message)
    {
        message = null;

        // 1. Check running process main module
        var procNames = platform switch
        {
            SocialPlatform.WeChat => new[] { "WeChat", "Weixin" },
            SocialPlatform.DingTalk => new[] { "DingTalk" },
            SocialPlatform.Feishu => new[] { "Feishu", "Lark" },
            SocialPlatform.WeCom => new[] { "WXWork" },
            _ => Array.Empty<string>()
        };

        foreach (var name in procNames)
        {
            var procs = Process.GetProcessesByName(name);
            foreach (var p in procs)
            {
                try
                {
                    var exe = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                    {
                        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                        message = exe;
                        return true;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        // 2. Check known executable paths and registry
        var candidatePaths = GetCandidatePaths(platform);
        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    message = path;
                    return true;
                }
                catch { }
            }
        }

        // 3. Fallback to protocol URI
        var uri = platform switch
        {
            SocialPlatform.DingTalk => "dingtalk://",
            SocialPlatform.Feishu => "feishu://",
            SocialPlatform.WeCom => "wxwork://",
            SocialPlatform.WeChat => "weixin://",
            _ => null
        };

        if (uri != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                message = uri;
                return true;
            }
            catch { }
        }

        return false;
    }

    private static IEnumerable<string> GetCandidatePaths(SocialPlatform platform)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        switch (platform)
        {
            case SocialPlatform.WeChat:
                yield return Path.Combine(programFiles, "Tencent", "WeChat", "WeChat.exe");
                yield return Path.Combine(programFilesX86, "Tencent", "WeChat", "WeChat.exe");
                if (ReadRegistryString(Registry.CurrentUser, @"Software\Tencent\WeChat", "InstallPath") is { } wxPath)
                {
                    yield return Path.Combine(wxPath, "WeChat.exe");
                }
                break;

            case SocialPlatform.DingTalk:
                yield return Path.Combine(localAppData, "DingTalk", "DingtalkLauncher.exe");
                yield return Path.Combine(programFilesX86, "DingDing", "DingtalkLauncher.exe");
                yield return Path.Combine(programFiles, "DingDing", "DingtalkLauncher.exe");
                if (ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\DingTalk.exe", "") is { } dtPath)
                {
                    yield return dtPath;
                }
                break;

            case SocialPlatform.Feishu:
                yield return Path.Combine(localAppData, "Feishu", "app", "Feishu.exe");
                yield return Path.Combine(localAppData, "Lark", "app", "Lark.exe");
                yield return Path.Combine(programFiles, "Feishu", "Feishu.exe");
                if (ReadRegistryString(Registry.CurrentUser, @"Software\Feishu", "InstallPath") is { } fsPath)
                {
                    yield return Path.Combine(fsPath, "Feishu.exe");
                }
                break;

            case SocialPlatform.WeCom:
                yield return Path.Combine(programFilesX86, "WXWork", "WXWork.exe");
                yield return Path.Combine(programFiles, "WXWork", "WXWork.exe");
                if (ReadRegistryString(Registry.CurrentUser, @"Software\Tencent\WXWork", "Executable") is { } wwPath)
                {
                    yield return wwPath;
                }
                if (ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Tencent\WXWork", "Executable") is { } wwPath64)
                {
                    yield return wwPath64;
                }
                break;
        }
    }

    private static string? ReadRegistryString(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
