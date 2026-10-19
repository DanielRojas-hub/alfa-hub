# Alfa Hub

Timer de ciclos para Tibia (basado en el "Peixe Timer" de cueistoreservices.com.br/timer).

- **Overlay**: solo el contador, encima de Tibia y únicamente mientras Tibia está al frente.
  Los clics lo atraviesan y llegan al juego.
- **Hub**: Iniciar/Pausar, Reiniciar, tiempo entre ciclos, alertas, sonido, colores,
  tamaño/opacidad/posición del overlay y atajos editables.

## Windows

Descarga `AlfaHub.exe` desde la última **Release** del repositorio. No necesita instalación
(usa .NET Framework 4.8, que ya viene en Windows 10/11).

1. Copia `AlfaHub.exe` a cualquier carpeta y ábrelo.
2. Como no está firmado, Windows puede mostrar "Windows protegió su PC":
   pulsa **Más información → Ejecutar de todas formas**.
3. Al cerrar el hub, Alfa Hub sigue en la bandeja del sistema (junto al reloj);
   desde ahí se vuelve a abrir o se sale.

Atajos por defecto: **Ctrl+Alt+P** iniciar/pausar, **Ctrl+Alt+R** reiniciar, **Ctrl+Alt+O** mostrar/ocultar overlay.
Los ajustes se guardan en `%APPDATA%\AlfaHub\ajustes.ini`.

Si el overlay no se ve encima de Tibia, pon Tibia en modo ventana o pantalla completa sin bordes
(una pantalla completa exclusiva no deja dibujar ventanas encima).

### BattlEye

Alfa Hub es un programa aparte que **no toca el cliente de Tibia**:

- no abre el proceso de Tibia, no lee ni escribe su memoria, no inyecta nada y no instala hooks;
- no envía teclas ni clics al juego (no es un macro ni un bot);
- para saber si Tibia está al frente solo mira el nombre del proceso y el título de la ventana
  activa, igual que el Administrador de tareas;
- los atajos son atajos globales normales de Windows (`RegisterHotKey`).

Aun así, ni CipSoft ni BattlEye publican una lista de programas permitidos, así que nadie puede
garantizarlo al 100 %. La única confirmación definitiva es preguntar al soporte de CipSoft.

## macOS

`macos/Alfa Hub.app` (se genera con `macos/build.sh`). Igual que en Windows, no toca el cliente:
solo consulta qué app está al frente y si tiene una ventana visible.

Atajos por defecto: **⌃⌥P**, **⌃⌥R**, **⌃⌥O**.

## Ícono y personaje

El personaje sale del [Outfiter de TibiaWiki](https://tibia.fandom.com/wiki/Outfiter?o=235&a1&c1=95&c2=95&c3=95&c4=114)
(`assets/outfit64.png`). `swift assets/make_icons.swift` lo recorta, lo centra y genera
`macos/AppIcon.icns`, `windows/AlfaHub.ico` y el sprite del hub. El ícono no lleva fondo, igual que
el de Tibia: en macOS el sistema le pone la misma placa gris.

## Compilar

- macOS: `macos/build.sh` (requiere Xcode).
- Windows: `dotnet build -c Release` dentro de `windows/` (funciona también desde macOS/Linux con el SDK de .NET).
