$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
[xml]$doc = Get-Content -LiteralPath 'D:\work\desktop-memo\ui.xaml' -Raw -Encoding UTF8
$reader = New-Object System.Xml.XmlNodeReader $doc
$window = [System.Windows.Markup.XamlReader]::Load($reader)
"parsed title={0} width={1} height={2}" -f $window.Title, $window.Width, $window.Height
