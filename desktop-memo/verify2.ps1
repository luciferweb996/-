param([string]$TargetPath)

Add-Type -Path 'D:\work\desktop-memo\wu.cs'

$global:tp = $null
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $TargetPath } | ForEach-Object { $global:tp = $_.ProcessId }
if (-not $global:tp) { Get-Process | Where-Object { $_.Path -eq $TargetPath } | ForEach-Object { $global:tp = $_.Id } }
if (-not $global:tp) { Write-Output 'APP_NOT_RUNNING'; exit }
Write-Output ('TargetPID=' + $global:tp)

$global:results = New-Object System.Collections.Generic.List[string]

$proc = [WU+EnumProc]{
  param($h, $lp)
  $po = 0
  [void][WU]::GetWindowThreadProcessId($h, [ref]$po)
  if ($po -ne $global:tp) { return $true }
  $sb = New-Object System.Text.StringBuilder 256
  [void][WU]::GetWindowText($h, $sb, 256)
  $ex = [WU]::GetWindowLong($h, -20)
  $vis = [WU]::IsWindowVisible($h)
  $rc = New-Object WU+RECT
  [void][WU]::GetWindowRect($h, [ref]$rc)
  $line = ('HWnd=0x' + $h.ToString('X') + ' Title=[' + $sb.ToString() + '] ExStyle=0x' + $ex.ToString('X8') + ' Visible=' + $vis + ' Rect=' + $rc.L + ',' + $rc.T + ',' + $rc.R + ',' + $rc.B)
  $global:results.Add($line)
  $global:results.Add(('TOOLWINDOW=' + (($ex -band 0x80) -ne 0) + ' TOPMOST=' + (($ex -band 0x8) -ne 0)))
  return $true
}
[void][WU]::EnumWindows($proc, [IntPtr]::Zero)

Write-Output ('RESULTS_COUNT=' + $global:results.Count)
foreach ($r in $global:results) { Write-Output $r }
