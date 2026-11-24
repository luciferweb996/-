param([int]$AppPid)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$ae = [System.Windows.Automation.AutomationElement]
$ts = [System.Windows.Automation.TreeScope]
$condAll = [System.Windows.Automation.Condition]::TrueCondition

$root = $ae::RootElement
$wins = $root.FindAll($ts::Children, $condAll)
$app = $null
foreach ($w in $wins) {
    if ($w.Current.ProcessId -eq $AppPid -and $w.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        $app = $w; break
    }
}
if ($app -eq $null) { Write-Output 'APP_WINDOW_NOT_FOUND'; exit }

$desc = $app.FindAll($ts::Descendants, $condAll)
Write-Output ("DESCENDANTS=" + $desc.Count)
foreach ($d in $desc) {
    $id = $d.Current.AutomationId
    $nm = $d.Current.Name
    $r = $d.Current.BoundingRectangle
    if ([double]::IsInfinity($r.X) -or [double]::IsInfinity($r.Y)) { continue }
    $ct = $d.Current.ControlType.ProgrammaticName
    if ($id -match 'Complete' -or $nm -match 'Complete' -or $id -eq 'ContentBox' -or $id -eq 'TitleBox') {
        Write-Output ("ID=[" + $id + "] NAME=[" + $nm + "] TYPE=" + $ct +
            " RECT=" + [int]$r.X + ',' + [int]$r.Y + ',' + [int]$r.Width + ',' + [int]$r.Height +
            " OFFSCREEN=" + $d.Current.IsOffscreen)
    }
}
