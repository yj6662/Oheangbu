# Trailer 4 (2026-10-12): after an editor restart Windows may pop a Firewall notification dialog (PickerHost.exe
# FirewallNotificationDialogServer). It takes the foreground, the game gets no input, every take fails with "OK 0/1; no HIT".
# This closes that dialog (same as pressing Cancel: no rule is added, nothing is allowed) and brings the Unity main window
# to the front, for up to <seconds> (default 45). Prints what it did and the final foreground title. Exit 0 = Unity in front.
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Trailer308/focus_unity.ps1 [seconds]
param([int]$Seconds = 45)
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class FU308 {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern IntPtr GetWindowThreadProcessId(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
'@
function FgTitle { $h = [FU308]::GetForegroundWindow(); $sb = New-Object System.Text.StringBuilder 512; [void][FU308]::GetWindowText($h, $sb, 512); $sb.ToString() }
function FgIsUnity { $h = [FU308]::GetForegroundWindow(); $pid2 = [IntPtr]::Zero; $buf = [System.Runtime.InteropServices.Marshal]::AllocHGlobal(4)
  [void][FU308]::GetWindowThreadProcessId($h, $buf); $id = [System.Runtime.InteropServices.Marshal]::ReadInt32($buf); [System.Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
  $pr = Get-Process -Id $id -ErrorAction SilentlyContinue; return ($pr -and $pr.ProcessName -eq 'Unity') }
$deadline = (Get-Date).AddSeconds($Seconds); $closed = 0
do {
  $dialogs = Get-CimInstance Win32_Process -Filter "Name='PickerHost.exe'" | Where-Object { $_.CommandLine -like '*FirewallNotificationDialogServer*' }
  foreach ($d in $dialogs) { Stop-Process -Id $d.ProcessId -Force -ErrorAction SilentlyContinue; $closed++; Write-Output ("firewall dialog closed pid " + $d.ProcessId) }
  $t = FgTitle
  if (FgIsUnity) {   # a Unity.exe window is in front; prefer the main window (same process, so SetForegroundWindow is allowed)
    if ($t -notlike 'Oheangbu -*') { $m = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like 'Oheangbu -*' } | Select-Object -First 1
      if ($m) { [void][FU308]::SetForegroundWindow($m.MainWindowHandle); Start-Sleep -Milliseconds 500 } }
    break }
  $u = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like 'Oheangbu -*' } | Select-Object -First 1
  if ($u) {
    $h = $u.MainWindowHandle
    $fg = [FU308]::GetForegroundWindow()
    $fgThread = [FU308]::GetWindowThreadProcessId($fg, [IntPtr]::Zero); $me = [FU308]::GetCurrentThreadId()
    [void][FU308]::AttachThreadInput([uint32]$fgThread, $me, $true)
    [void][FU308]::ShowWindow($h, 9)   # SW_RESTORE (no-op when not minimized)
    [void][FU308]::SetForegroundWindow($h)
    [void][FU308]::AttachThreadInput([uint32]$fgThread, $me, $false)
    try { $sh = New-Object -ComObject WScript.Shell; [void]$sh.AppActivate($u.Id) } catch {}
  }
  Start-Sleep -Milliseconds 1500
} while ((Get-Date) -lt $deadline)
$t = FgTitle; $ok = FgIsUnity
Write-Output ("focus_unity: dialogs closed " + $closed + " | foreground: " + $t + " | unity=" + $ok)
if ($ok) { exit 0 } else { exit 1 }
