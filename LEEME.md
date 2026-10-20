# Alfa Hub

Timers para Tibia con overlay (basado en los timers de cueistoreservices.com.br/timer), para macOS y Windows.

- **Timers**: varios a la vez, con presets **Pez** (cada 2:00), **Bakragore** (mecánica cada 1:30
  durante 25 min), **Pilar** (rotación de los Totens cada 6 s) o personalizados. Cada uno tiene su
  duración, avisos, sonido propio (.wav/.mp3 o beep) y atajo de iniciar/pausar.
- **Espejos**: copia partes de la ventana de Tibia (cooldowns, hechizos, barra de vida…) en ventanitas
  flotantes que pones donde quieras; solo se ven con Tibia al frente y dejan pasar los clics.
- **Overlay**: los contadores de los timers en marcha, encima de Tibia y únicamente mientras Tibia
  está al frente. Los clics lo atraviesan y llegan al juego.
- **Hub**: barra lateral con Timers, Overlay, Atajos y Sonido (ver [docs/DISENO-REFERENCIA.md](docs/DISENO-REFERENCIA.md)).

## Windows

Descarga `AlfaHub.exe` desde la última **Release** del repositorio. No necesita instalación
(usa .NET Framework 4.8, que ya viene en Windows 10/11).

1. Copia `AlfaHub.exe` a cualquier carpeta y ábrelo.
2. Como no está firmado, Windows puede mostrar "Windows protegió su PC":
   pulsa **Más información → Ejecutar de todas formas**.
3. Al cerrar el hub, Alfa Hub sigue en la bandeja del sistema (junto al reloj);
   desde ahí se vuelve a abrir o se sale.

Atajos por defecto: **Ctrl+Alt+P** iniciar/pausar el timer Pez, **Ctrl+Alt+R** reiniciar el último
timer usado, **Ctrl+Alt+O** mostrar/ocultar overlay. Cada timer puede tener su propio atajo.
Los ajustes se guardan en `%APPDATA%\AlfaHub\ajustes.ini` y los timers en `timers.json`.

Si el overlay no se ve encima de Tibia, pon Tibia en modo ventana o pantalla completa sin bordes
(una pantalla completa exclusiva no deja dibujar ventanas encima).

### BattlEye

Alfa Hub es un programa aparte que **no toca el cliente de Tibia**:

- no abre el proceso de Tibia, no lee ni escribe su memoria, no inyecta nada y no instala hooks;
- no envía teclas ni clics al juego (no es un macro ni un bot);
- para saber si Tibia está al frente solo mira el nombre del proceso y el título de la ventana
  activa, igual que el Administrador de tareas;
- los atajos son atajos globales normales de Windows (`RegisterHotKey`);
- los **espejos** usan las miniaturas de ventana de Windows (DWM, las mismas de la vista previa de
  la barra de tareas): las dibuja el sistema dentro de la ventana del espejo; Alfa Hub no captura la
  pantalla ni lee píxeles del cliente.

Aun así, ni CipSoft ni BattlEye publican una lista de programas permitidos, así que nadie puede
garantizarlo al 100 %. La única confirmación definitiva es preguntar al soporte de CipSoft.

## macOS

`macos/Alfa Hub.app` (se genera con `macos/build.sh`). Igual que en Windows, no toca el proceso del
cliente: consulta qué app está al frente y si tiene una ventana visible. Los **espejos** copian la
ventana de Tibia con ScreenCaptureKit, de una de estas dos formas (se elige en la página Espejos):

- **Selector del sistema** (por defecto): eliges la ventana de Tibia en el selector de macOS. No pide
  el permiso de Grabación de pantalla ni muestra el aviso periódico de macOS, pero hay que elegirla
  cada vez que abres Alfa Hub o reinicias Tibia.
- **Automática**: Alfa Hub encuentra la ventana solo; necesita el permiso de **Grabación de pantalla**
  ("Abrir Ajustes" añade la app a la lista: solo hay que activarla) y macOS lo reconfirma de vez en cuando.

Atajos por defecto: **⌃⌥P** (timer Pez), **⌃⌥R** (reiniciar el último timer usado), **⌃⌥O** (overlay).

## Ícono y personaje

El personaje sale del [Outfiter de TibiaWiki](https://tibia.fandom.com/wiki/Outfiter?o=235&a1&c1=95&c2=95&c3=95&c4=114)
(`assets/outfit64.png`). `swift assets/make_icons.swift` lo recorta, lo centra y genera
`macos/AppIcon.icns`, `windows/AlfaHub.ico` y el sprite del hub. El ícono no lleva fondo, igual que
el de Tibia: en macOS el sistema le pone la misma placa gris.

## Compilar

- macOS: `macos/build.sh` (requiere Xcode).
- Windows: `dotnet build -c Release` dentro de `windows/` (funciona también desde macOS/Linux con el SDK de .NET).
- El workflow *Capturas Windows* compila el `.exe` en un Windows real, lo abre, recorre el hub y sube
  capturas como artefacto (se lanza solo al cambiar `windows/` o `ci/`).
