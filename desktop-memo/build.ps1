$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$out = 'D:\work\desktop-memo\memo-build.exe'
$args = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    ('/out:' + $out),
    '/win32icon:D:\work\desktop-memo\app.ico',
    '/win32manifest:D:\work\desktop-memo\app.manifest',
    '/resource:D:\work\desktop-memo\ui.xaml,ui.xaml',
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Xml.dll',
    '/reference:System.Xml.Linq.dll',
    ('/reference:' + $wpf + '\WindowsBase.dll'),
    ('/reference:' + $wpf + '\PresentationCore.dll'),
    ('/reference:' + $wpf + '\PresentationFramework.dll'),
    ('/reference:' + $fw + '\System.Xaml.dll'),
    'D:\work\desktop-memo\Program.cs'
)
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Get-Item -LiteralPath $out | Select-Object FullName, Length, LastWriteTime
