param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 1920,
    [int]$H = 1080
)
$ErrorActionPreference = 'Stop'
$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class Cap {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static void Shot(string path, int x, int y, int w, int h) {
    SetProcessDPIAware();
    using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
    using (var g = Graphics.FromImage(bmp)) {
      g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
      bmp.Save(path, ImageFormat.Png);
    }
  }
}
'@
if (-not ('Cap' -as [type])) {
    Add-Type -TypeDefinition $code -Language CSharp -ReferencedAssemblies System.Drawing
}
[Cap]::Shot($Path, $X, $Y, $W, $H)
Get-Item -LiteralPath $Path | Select-Object Name, Length
