param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y
)
$ErrorActionPreference = 'Stop'
$code = @'
using System;
using System.Runtime.InteropServices;
public class M {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  public static void Click(int x, int y) {
    SetProcessDPIAware();
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(200);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(90);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
}
'@
if (-not ('M' -as [type])) {
    Add-Type -TypeDefinition $code -Language CSharp
}
[M]::Click($X, $Y)
"clicked $X,$Y"
