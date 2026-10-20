param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$FixtureRoot)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExecutable = [IO.Path]::GetFullPath($Executable)
$taskFixtures = [IO.Path]::GetFullPath($FixtureRoot)
if (!$taskExecutable.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    !$taskFixtures.StartsWith((Join-Path $taskRoot 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '启动验证仅可使用本项目程序与隔离预览样例。' }
$taskRemembered = Get-ChildItem -LiteralPath $taskFixtures -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'remembered.protected') } | Select-Object -First 1
if (!$taskRemembered) { throw '请先运行 WPF 验证生成独立的记住登录样例。' }
if (!('E2Day.StartupEntryProbe' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace E2Day {
  public class ProbeWindow { public IntPtr Handle; public string Title; public int Width; public int Height; }
  public static class StartupEntryProbe {
    private delegate bool Callback(IntPtr handle, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle, uint message, IntPtr first, IntPtr second);
    public static ProbeWindow[] Windows(int processId) {
      var windows = new List<ProbeWindow>();
      EnumWindows((handle,state) => {
        uint owner; GetWindowThreadProcessId(handle,out owner);
        if(owner==(uint)processId) {
          var text=new StringBuilder(512); GetWindowText(handle,text,text.Capacity);
          if(text.Length>0 && IsWindowVisible(handle)) { Rect rect; GetWindowRect(handle,out rect); SetWindowPos(handle,IntPtr.Zero,-20000,-20000,0,0,0x15); windows.Add(new ProbeWindow {Handle=handle,Title=text.ToString(),Width=rect.Right-rect.Left,Height=rect.Bottom-rect.Top}); }
        }
        return true;
      },IntPtr.Zero);
      return windows.ToArray();
    }
  }
}
'@
}
foreach ($taskCase in @('first-launch-click','remembered-auto')) {
    $taskData = Join-Path $taskRoot ('.build/startup-entry-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $taskData | Out-Null
    if ($taskCase -eq 'remembered-auto') { Copy-Item -Path (Join-Path $taskRemembered.FullName '*') -Destination $taskData -Recurse }
    $taskProcess = Start-Process -FilePath $taskExecutable -ArgumentList @('--data-dir', ('"{0}"' -f $taskData)) -WindowStyle Hidden -PassThru
    try {
        $taskWatch = [Diagnostics.Stopwatch]::StartNew()
        $taskSawOpening = $false; $taskSawDestination = $false
        $taskObserved = [Collections.Generic.HashSet[string]]::new()
        $taskExampleAccount = @(Get-Content -LiteralPath (Join-Path $taskRemembered.FullName 'accounts.json') -Raw -Encoding UTF8 | ConvertFrom-Json)[0]
        $taskExpected = if ($taskCase -eq 'first-launch-click') { 'E²-Day · 账号登录' } else { 'E²-Day · ' + $taskExampleAccount.Name }
        while ($taskWatch.Elapsed.TotalSeconds -lt 20 -and !$taskProcess.HasExited) {
            $taskWindows = @([E2Day.StartupEntryProbe]::Windows($taskProcess.Id))
            foreach ($taskWindow in $taskWindows) { $null = $taskObserved.Add($taskWindow.Title) }
            $taskOpening = $taskWindows | Where-Object { $_.Title -ceq 'E²-Day · 探索每一天' } | Select-Object -First 1
            if ($taskOpening -and !$taskSawOpening) {
                $taskSawOpening = $true
                $taskInitialWindow = $taskOpening
                if ($taskOpening.Width -lt 1000 -or $taskOpening.Height -lt 650) { throw '开场第一帧仍使用小窗口。' }
                if ($taskCase -eq 'first-launch-click') {
                    $null = [E2Day.StartupEntryProbe]::PostMessage($taskOpening.Handle,0x0201,[IntPtr]1,[IntPtr]((210 -shl 16) -bor 250))
                    $null = [E2Day.StartupEntryProbe]::PostMessage($taskOpening.Handle,0x0202,[IntPtr]0,[IntPtr]((210 -shl 16) -bor 250))
                }
            }
            $taskDestination = $taskWindows | Where-Object { $_.Title -ceq $taskExpected } | Select-Object -First 1
            if ($taskDestination) {
                if (!$taskSawOpening -or $taskDestination.Handle -ne $taskInitialWindow.Handle -or
                    $taskDestination.Width -ne $taskInitialWindow.Width -or $taskDestination.Height -ne $taskInitialWindow.Height) { throw '开场到目标页面发生了窗口替换或尺寸跳变。' }
                $taskSawDestination = $true
                Start-Sleep -Milliseconds 120
                if (@([E2Day.StartupEntryProbe]::Windows($taskProcess.Id) | Where-Object { $_.Title -ceq 'E²-Day · 探索每一天' }).Count) { throw '目标页面出现后开场窗口仍未关闭。' }
                $null = [E2Day.StartupEntryProbe]::PostMessage($taskDestination.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
                break
            }
            Start-Sleep -Milliseconds 20
        }
        if (!$taskSawOpening -or !$taskSawDestination) { throw "实际启动流程未到达预期窗口：$taskCase；观测到：$($taskObserved -join '、')" }
        if (!$taskProcess.WaitForExit(10000) -or $taskProcess.ExitCode -ne 0) { throw "启动流程关闭异常：$taskCase" }
        if (Test-Path -LiteralPath (Join-Path $taskData 'errors.log')) { throw "实际启动产生错误日志：$taskCase" }
        Write-Output "PASS actual entry $taskCase -> $taskExpected; full-size opening; same HWND and dimensions; input consumed; normal close; isolated example account only"
    }
    finally { if (!$taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() } }
}
