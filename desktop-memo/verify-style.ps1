param()
# ASCII-only, STA. Verify round-12 PURE single-colour button WITHOUT depending on
# the physical display. Load the REAL ui.xaml via XamlReader, apply the real
# CompleteCircleStyle, and read (a) shared page brushes, (b) the ONE template
# Ellipse's effective Fill for each Tag state, (c) the full trigger map, and
# (d) assert there is exactly ONE coloured shape, no shadow, no hover/press tint.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml

$xaml=[System.IO.File]::ReadAllText('D:\work\desktop-memo\ui.xaml',[System.Text.Encoding]::UTF8)
$win=[System.Windows.Markup.XamlReader]::Parse($xaml)
$style=$win.Resources['CompleteCircleStyle']
if($style -eq $null){ Write-Output 'STYLE_NOT_FOUND'; exit }

function ColOf($b){
  if($b -is [System.Windows.Media.SolidColorBrush]){ $c=$b.Color; return '#'+$c.R.ToString('X2')+$c.G.ToString('X2')+$c.B.ToString('X2') }
  return '['+$b.GetType().Name+']'
}

# (a) shared page brushes -- the "same brown / same green" requirement
$accent=$win.Resources['AccentBrush']; $shortB=$win.Resources['ShortBrush']; $green=$win.Resources['LongForeverBrush']
Write-Output ('PAGE  AccentBrush='+(ColOf $accent)+'  ShortBrush='+(ColOf $shortB)+'  LongForeverBrush='+(ColOf $green))

# (b) base setters of the real style (pure design: no Background/BorderBrush base)
Write-Output 'STYLE BASE SETTERS (pure design):'
foreach($s in $style.Setters){
  if($s.Property.Name -eq 'Foreground' -or $s.Property.Name -eq 'BorderThickness'){
    Write-Output ('  '+$s.Property.Name+' = '+$s.Value)
  }
}

# (c) EFFECTIVE Fill read off the ONE applied-template Ellipse, per Tag state
$btn=New-Object System.Windows.Controls.Button
$btn.Style=$style; $btn.Width=56; $btn.Height=56
$sp=New-Object System.Windows.Controls.StackPanel
$sp.Orientation='Vertical'; $sp.HorizontalAlignment='Center'; $sp.VerticalAlignment='Center'
$i=New-Object System.Windows.Controls.TextBlock
$i.Text=[string]([char]0x25CB); $i.FontSize=15; $i.TextAlignment='Center'
$t=New-Object System.Windows.Controls.TextBlock
$t.Text=[string]([char]0x5F85); $t.FontSize=12.5; $t.TextAlignment='Center'
$sp.Children.Add($i)|Out-Null; $sp.Children.Add($t)|Out-Null
$btn.Content=$sp
$test=New-Object System.Windows.Window
$test.Width=160; $test.Height=160; $test.WindowStyle='None'; $test.Background=[System.Windows.Media.Brushes]::White
$test.Content=$btn
$test.Show()
function Pump(){ [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Render,[System.Action]{}) }
Pump
$btn.ApplyTemplate()|Out-Null
$Disc=$btn.Template.FindName('Disc',$btn)
if($Disc -eq $null){ Write-Output 'DISC_NOT_FOUND'; exit }
Write-Output ('PURE CHECK  Disc.Effect = '+$(if($Disc.Effect -eq $null){'null (no shadow)'}else{$Disc.Effect.GetType().Name}))
Write-Output 'EFFECTIVE Fill ON THE ONE TEMPLATE ELLIPSE (default / hover / pressed identical):'
foreach($tag in @('Pending','Done')){
  $btn.Tag=$tag; Pump; Start-Sleep -Milliseconds 80
  Write-Output ('  Tag='+$tag+'  Disc.Fill='+(ColOf $Disc.Fill))
}

# (d) trigger map + PURE assertions (no colour setter for hover/pressed/focused)
Write-Output 'TRIGGER MAP (resolved brush values from real XAML):'
$colorSetterCount=0
$badSubState=0
foreach($tr in $btn.Template.Triggers){
  $label=''
  $isColorTrigger=$false
  if($tr -is [System.Windows.MultiTrigger]){
    $parts=@(); foreach($c in $tr.Conditions){ $parts+=($c.Property.Name+'='+$c.Value) }
    $label='MultiTrigger['+($parts -join ' && ')+']'
  } elseif($tr -is [System.Windows.Trigger]){
    $label='Trigger['+$tr.Property.Name+'='+$tr.Value+']'
  } else { $label=$tr.GetType().Name }
  foreach($s in $tr.Setters){
    $col=ColOf $s.Value
    if($col -like '[[]*'){ $col=[string]$s.Value }
    Write-Output ('  '+$label+' -> '+$s.TargetName+'.'+$s.Property.Name+' = '+$col)
    if($s.Property.Name -eq 'Fill'){ $colorSetterCount++ }
  }
  # PURE: the ONLY colour triggers are Tag=Pending and Tag=Done (no IsMouseOver /
  # IsPressed / IsKeyboardFocused / IsFocused colour setters anywhere)
  if($tr -is [System.Windows.Trigger] -and $tr.Property.Name -eq 'Tag' -and ($tr.Value -eq 'Pending' -or $tr.Value -eq 'Done')){
    # allowed
  } elseif($tr -is [System.Windows.Trigger] -and $tr.Property.Name -eq 'IsEnabled'){
    # allowed (opacity only)
  } else {
    foreach($s in $tr.Setters){ if($s.Property.Name -eq 'Fill'){ $badSubState++ } }
  }
}
Write-Output ('PURE ASSERT  Fill-setter count='+$colorSetterCount+' (expect 2, one per state)  stray hover/press/focus color setters='+$badSubState+' (expect 0)')
Write-Output ('PURE RESULT = '+($colorSetterCount -eq 2 -and $badSubState -eq 0))
Write-Output 'DONE'
