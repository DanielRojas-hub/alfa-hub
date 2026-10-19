# Abre AlfaHub.exe en Windows, recorre el hub con UI Automation y guarda capturas.
# Lo usa el workflow .github/workflows/capturas-windows.yml (Windows PowerShell 5.1).
#
#   powershell -File ci/capturas-windows.ps1 -Exe windows/bin/Release/net48/AlfaHub.exe -Out capturas

param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Shot
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    // Captura la ventana completa aunque se salga de la pantalla.
    public static void Window(IntPtr h, string path)
    {
        RECT r; GetWindowRect(h, out r);
        using (var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(h, dc, 2);  // PW_RENDERFULLCONTENT
                g.ReleaseHdc(dc);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    public static void Screen(string path)
    {
        Rectangle b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        using (var bmp = new Bitmap(b.Width, b.Height))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    public static string Size(IntPtr h)
    {
        RECT r; GetWindowRect(h, out r);
        return string.Format("{0}x{1} en ({2},{3})", r.Right - r.Left, r.Bottom - r.Top, r.Left, r.Top);
    }
}
"@

$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

function Find-ByName($root, [string] $name, [int] $timeoutSeconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $element = $root.FindFirst($Scope::Descendants, $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 300
    }
    throw "No se encontró el elemento '$name'"
}

function Click($name) {
    $element = Find-ByName $script:hub $name
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 800
}

function Toggle($name) {
    $element = Find-ByName $script:hub $name
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 800
}

function Capture-Hub($file) {
    [Shot]::Window($script:hwnd, (Join-Path $Out $file))
    Write-Host "captura: $file"
}

function Assert-Running {
    if ($script:process.HasExited) { throw "AlfaHub.exe se cerró solo (código $($script:process.ExitCode))" }
}

Write-Host "Pantalla: $([System.Windows.Forms.Screen]::PrimaryScreen.Bounds)"
$script:process = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
Start-Sleep -Seconds 5
Assert-Running

$script:hub = Find-ByName $UIA::RootElement 'Alfa Hub' 20
$script:hwnd = [IntPtr] $script:hub.Current.NativeWindowHandle
Write-Host "Hub: $([Shot]::Size($script:hwnd))"
# Arriba del todo: PrintWindow captura la ventana entera aunque no quepa en la pantalla del runner.
[void] [Shot]::SetWindowPos($script:hwnd, [IntPtr]::Zero, 40, 0, 0, 0, 0x5)  # mover (NOSIZE | NOZORDER)
[void] [Shot]::SetForegroundWindow($script:hwnd)
Start-Sleep -Seconds 1

Capture-Hub '01-hub-ciclo-y-sonido.png'
Click 'Overlay'
Capture-Hub '02-hub-overlay.png'
Click 'Atajos'
Capture-Hub '03-hub-atajos.png'
Click 'Ciclo y sonido'

# Timer en marcha y en alerta (ciclo de 1:00: la alerta empieza a los 35 s).
Click '01:00'
Click '▶  Iniciar'
Start-Sleep -Seconds 3
Capture-Hub '04-tarjeta-en-marcha.png'
Start-Sleep -Seconds 35
Capture-Hub '05-tarjeta-alerta.png'
Assert-Running

# El overlay solo aparece con Tibia al frente; desbloqueado se ve siempre (borde ámbar).
Click 'Overlay'
Toggle 'Desbloquear para moverlo'
Start-Sleep -Seconds 2
[Shot]::Screen((Join-Path $Out '06-pantalla-overlay-desbloqueado.png'))
Write-Host 'captura: 06-pantalla-overlay-desbloqueado.png'
Toggle 'Desbloquear para moverlo'
Start-Sleep -Seconds 1
[Shot]::Screen((Join-Path $Out '07-pantalla-overlay-bloqueado.png'))
Write-Host 'captura: 07-pantalla-overlay-bloqueado.png (sin Tibia, el overlay no debe verse)'

Assert-Running
Stop-Process -Id $script:process.Id
Write-Host 'Listo.'
