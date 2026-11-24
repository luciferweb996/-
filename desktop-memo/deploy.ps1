param()
# ASCII-only script. Chinese exe name built from Unicode code points so PS5.1's
# GBK decoding of .ps1 files cannot corrupt the path (documented pitfall).
$ErrorActionPreference = 'Stop'
$name = ''
foreach ($c in 0x684C,0x9762,0x5907,0x5FD8,0x5F55) { $name += [char]$c }
$name += '.exe'
$desktop = 'C:\Users\guojiamyu\Desktop'
$target  = Join-Path $desktop $name
$src     = 'D:\work\desktop-memo\memo-build.exe'
$wsExe   = Join-Path 'D:\work\desktop-memo' $name

# Stop any running instance holding the single-instance mutex (both deployed + workspace copies).
$procs = Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -eq $target -or $_.ExecutablePath -eq $wsExe
}
foreach ($p in $procs) {
    "STOPPID=$($p.ProcessId) PATH=$($p.ExecutablePath)"
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 500

Copy-Item -LiteralPath $src -Destination $target -Force
Copy-Item -LiteralPath $src -Destination $wsExe -Force
$fi = Get-Item -LiteralPath $target
"DEPLOYED=$($fi.FullName) SIZE=$($fi.Length)"

# Launch independently of the launcher process (WMI Win32_Process.Create is the
# only reliable persistent launch method in this environment).
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = '"' + $target + '"'
}
"LAUNCH_RETURN=$($r.ReturnValue) PID=$($r.ProcessId)"
