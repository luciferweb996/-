param([int]$AppPid)
# ASCII-only, NO non-ASCII bytes (PS5.1 reads .ps1 as GBK).
# Round 14 BIDIRECTIONAL check-in verification via UIA (no physical mouse).
# Start-state agnostic: detects the initial state, then performs 4 flips
# (two full round trips). On EVERY snapshot it checks:
#   - button text / icon switched to the opposite state
#   - BOTH states stay clickable (IsEnabled=True) -- key round-14 change
#   - status text (CompletedText) changed and matches the new state
#   - notes.xml data sync: count of completed="true" (and completedAt=) moves
#     by exactly +1 on PENDING->DONE and -1 on DONE->PENDING
# Leaves the app in the default PENDING state.
# Expected Chinese strings built from Unicode code points (no literals).
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$TODO      = [string]([char]0x5F85)+[string]([char]0x529E)
$DONE_TEXT = ([string]([char]0x5DF2))+([string]([char]0x5B8C))+([string]([char]0x6210))
$ICO_O     = [string]([char]0x25CB)
$ICO_V     = [string]([char]0x2713)
$ST_PEND   = ([string]([char]0x672A))+([string]([char]0x5B8C))+([string]([char]0x6210))
$ST_DONE   = ([string]([char]0x5B8C))+([string]([char]0x6210))

$xmlPath = Join-Path $env:APPDATA 'DesktopMemo\notes.xml'
function Log([string]$m){ [Console]::WriteLine($m) }

$ae=[System.Windows.Automation.AutomationElement]
$ts=[System.Windows.Automation.TreeScope]
$root=$ae::RootElement
$app=$null
foreach($w in $root.FindAll($ts::Children,[System.Windows.Automation.Condition]::TrueCondition)){
  if($w.Current.ProcessId -eq $AppPid -and $w.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window){ $app=$w; break }
}
if($app -eq $null){ Log 'APP_WINDOW_NOT_FOUND'; exit 1 }
function AE([string]$id){
  $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
  return $app.FindFirst($ts::Descendants,$c)
}
function ReadState {
  $btn=AE 'CompleteButton'; $txt=AE 'CompleteButtonText'; $ico=AE 'CompleteButtonIcon'; $st=AE 'CompleteStateText'
  $tv=if($txt -ne $null){[string]$txt.Current.Name}else{'NONE'}
  $iv=if($ico -ne $null){[string]$ico.Current.Name}else{'NONE'}
  $sv=if($st -ne $null){[string]$st.Current.Name}else{'NONE'}
  $en=if($btn -ne $null){[bool]$btn.Current.IsEnabled}else{$false}
  return @{ Text=$tv; Icon=$iv; State=$sv; Enabled=$en }
}
function KindOf($s){
  if($s.Text -ceq $TODO -and $s.Icon -ceq $ICO_O){ return 'PENDING' }
  if($s.Text -ceq $DONE_TEXT -and $s.Icon -ceq $ICO_V){ return 'DONE' }
  return 'UNKNOWN'
}
function ExpectStyle($s,[string]$kind){
  $okText  = if($kind -eq 'PENDING'){ $s.Text -ceq $TODO }    else { $s.Text -ceq $DONE_TEXT }
  $okIcon  = if($kind -eq 'PENDING'){ $s.Icon -ceq $ICO_O }    else { $s.Icon -ceq $ICO_V }
  $okState = if($kind -eq 'PENDING'){ $s.State -ceq $ST_PEND } else { $s.State.Contains($ST_DONE) }
  $okEn    = ($s.Enabled -eq $true)
  return @(($okText -and $okIcon -and $okState -and $okEn), $okText, $okIcon, $okState, $okEn)
}
function TryInvoke {
  $b=AE 'CompleteButton'
  if($b -eq $null){ return 'NO_BUTTON' }
  try {
    $inv=$b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $inv.Invoke()
    Start-Sleep -Milliseconds 600
    return 'INVOKED'
  } catch { return 'BLOCKED:'+$_.Exception.GetType().Name }
}
function ReadData {
  if(-not (Test-Path -LiteralPath $xmlPath)){ return @{ Comp='NOFILE'; At=-1 } }
  $raw = [System.IO.File]::ReadAllText($xmlPath, [System.Text.Encoding]::UTF8)
  $comp = ([regex]::Matches($raw, 'completed="true"')).Count
  $at   = ([regex]::Matches($raw, 'completedAt=')).Count
  return @{ Comp=$comp; At=$at }
}

Log ('LIVE APP PID='+$AppPid+' -- BIDIRECTIONAL check-in verification')
Log ('XML PATH='+$xmlPath)

$s=ReadState; $start=KindOf $s
Log ('T0 initial: kind='+$start+' text=['+$s.Text+'] icon=['+$s.Icon+'] state=['+$s.State+'] IsEnabled='+$s.Enabled)
if($start -eq 'UNKNOWN'){ Log 'INITIAL STATE UNRECOGNIZED'; exit 1 }
$e0=ExpectStyle $s $start
$d0=ReadData
Log ('   T0 styleOK='+$e0[0]+' (text='+$e0[1]+' icon='+$e0[2]+' stateDisplay='+$e0[3]+' clickable='+$e0[4]+')  data[completedTrue='+$d0.Comp+' completedAt='+$d0.At+']')
$allOK = $e0[0] -and ($d0.Comp -eq $d0.At)

$prev=$start; $prevState=$s.State; $prevData=$d0
for($i=1; $i -le 4; $i++){
  $r=TryInvoke
  $sn=ReadState; $kn=KindOf $sn
  $expected = if($prev -eq 'PENDING'){ 'DONE' } else { 'PENDING' }
  $e=ExpectStyle $sn $expected
  $d=ReadData
  $delta = if($expected -eq 'DONE'){ 1 } else { -1 }
  $dataOK = (($d.Comp - $prevData.Comp) -eq $delta) -and ($d.Comp -eq $d.At)
  $statusChanged = ($sn.State -cne $prevState)
  $sync = ($r -eq 'INVOKED') -and ($kn -ceq $expected) -and $e[0] -and $dataOK -and $statusChanged
  Log ('T'+$i+' flip ('+$r+'): kind='+$kn+' expect='+$expected+' text=['+$sn.Text+'] icon=['+$sn.Icon+'] state=['+$sn.State+'] IsEnabled='+$sn.Enabled)
  Log ('   styleOK='+$e[0]+' (text='+$e[1]+' icon='+$e[2]+' stateDisplay='+$e[3]+' clickable='+$e[4]+')  dataDelta='+($d.Comp - $prevData.Comp)+' expectDelta='+$delta+' dataOK='+$dataOK+' [completedTrue='+$d.Comp+' completedAt='+$d.At+']  statusChanged='+$statusChanged)
  $allOK = $allOK -and $sync
  $prev=$kn; $prevState=$sn.State; $prevData=$d; $s=$sn
}

if((KindOf $s) -eq 'DONE'){
  $r=TryInvoke; $sf=ReadState; $ef=ExpectStyle $sf 'PENDING'; $df=ReadData
  $ok=(($r -eq 'INVOKED') -and $ef[0] -and (($df.Comp - $prevData.Comp) -eq -1))
  Log ('SETTLE back to PENDING ('+$r+'): kind='+(KindOf $sf)+' IsEnabled='+$sf.Enabled+' dataOK='+(($df.Comp - $prevData.Comp) -eq -1))
  $allOK = $allOK -and $ok; $s=$sf
}
Log ('FINAL state: '+(KindOf $s)+' text=['+$s.Text+'] IsEnabled='+$s.Enabled)
Log ('RESULT: bidirectional-toggle PASS = '+$allOK)
Log 'DONE'
