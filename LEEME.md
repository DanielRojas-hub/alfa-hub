# Alfa Hub

Timers para Tibia con overlay (basado en los timers de cueistoreservices.com.br/timer), para macOS y Windows.

- **Hoy**: en qué ciudad está **Rashid** (y dónde encontrarlo), cuánto falta para el **server save**
  (con la hora local), cuándo termina la rotación del **Tibiadrome** y la **criatura y el boss
  boosteados** del día. Rashid, el server save y el Tibiadrome se calculan sin conexión; los
  boosteados vienen de la [API de TibiaData](https://tibiadata.com) y se guardan para verlos sin conexión.
- **Timers**: varios a la vez, con presets **Pez** (cada 2:00), **Bakragore** (mecánica cada 1:30
  durante 25 min), **Pilar** (rotación de los Totens cada 6 s) o personalizados. Cada uno tiene su
  duración, avisos, sonido (14 incluidos o un .wav/.mp3 propio), atajo de iniciar/pausar y su propio
  contador sobre Tibia (tamaño, opacidad, colores y sitio), que se edita en el editor del timer.
- **Tibia Audio**: pones la tecla con la que usas una habilidad en Tibia y su cooldown; al pulsarla en
  el juego empieza la cuenta y suena un aviso cuando la habilidad vuelve a estar lista. La tecla sigue
  llegando a Tibia (Alfa Hub solo la mira). Está apagado hasta que lo activas.
- **Espejos**: copia partes de la ventana de Tibia (cooldowns, hechizos, barra de vida…) en ventanitas
  flotantes que pones donde quieras; solo se ven con Tibia al frente y dejan pasar los clics.
- **Overlay**: el contador de cada timer en marcha, encima de Tibia y únicamente mientras Tibia
  está al frente. Los clics lo atraviesan y llegan al juego. "Desbloquear para moverlos" (página
  Timers) los muestra todos para arrastrar cada uno a su sitio.
- **Outfit** (en Windows; en macOS se edita en cada char de **Perfiles**): eliges tu personaje como en el [Outfiter de TibiaWiki](https://tibia.fandom.com/wiki/Outfiter)
  (outfit, hombre o mujer, addons, los cuatro colores del outfit y de la montura, orientación, montura
  y caminata animada). Con **Guardar cambios** pasa al ícono de la app (Dock o barra de tareas), a la
  barra lateral y a la barra de menús o bandeja; en **Mis outfits** guardas tus combinaciones para
  volver a ellas con un clic. Los sprites se descargan de la
  API de TibiaWiki la primera vez que eliges cada outfit o montura y quedan en caché. El ícono del
  archivo (.app / .exe) sigue siendo el original.
- **Perfiles** (por ahora solo en macOS): cada perfil junta varios chars. Al escribir el nombre de un
  char se **sincroniza con tibia.com** (TibiaData) y se vuelve a sincronizar al abrirlo si pasó una hora.
  Cada char tiene:
  - su **outfit**, que se edita como en el [Outfiter de TibiaWiki](https://tibia.fandom.com/wiki/Outfiter)
    (outfit, hombre o mujer, addons, colores, montura, caminata y *Mis outfits*). Con **Mostrar en el
    ícono de Alfa Hub** ese char pasa al ícono de la app (Dock o barra de tareas), a la barra lateral y a
    la barra de menús o bandeja;
  - **General**, al estilo de [guildstats.eu](https://guildstats.eu): nivel con el progreso al siguiente y
    el puesto en su mundo (highscores), HP, maná, capacidad y rango de party (calculados), cuenta, gremio,
    casas, muertes recientes y los otros chars de la cuenta (se añaden al perfil con un clic);
  - **Experiencia**, también como guildstats: mejor día, media diaria, puesto, gráfico de experiencia o
    nivel, experiencia ganada cada día, predicción de cuándo llegará a un nivel y la tabla por día. Alfa
    Hub guarda el nivel y la experiencia de cada char sincronizado una vez por día de Tibia (después del
    server save), aunque no lo abras; el historial empieza el día que se sincroniza por primera vez (los
    días anteriores no se pueden traer);
  - **Bestiary** completo (833 criaturas) en cuadrícula por clase, como el
    [Bestiary Tracker de tibiaroute](https://tibiaroute.com/bestiary-tracker): resumen, filtros de clase,
    dificultad y estado, orden por clase, nombre, charm points, progreso, HP o kills que faltan, y la ficha
    de cada criatura (etapas, kills, debilidades y dónde vive); y recomendaciones **Single** (criaturas que
    convienen solo según el nivel) y **Group** (zonas donde terminar varias a la vez);
  - **Charms**, como el [Charm Optimizer de hakaimarket](https://hakaimarket.com/calculators?tool=charmOptimizer):
    primero el optimizador, que con los charms que tiene, las criaturas de la hunt (una a una o toda una
    zona) y cuántas mata por hora reparte los charms de daño (elementales, Overpower y Overflux) con las
    fórmulas de TibiaWiki; y en *Mis charms*, los charm points del Bestiary y gastados y el nivel de cada
    charm;
  - **Inventario** en cuadrículas como en Tibia: el equipo en sus huecos y un contenedor por sitio
    (mochila, depot, casa, stash, otro char u otro sitio, agrupado por ciudad o detalle), con la imagen de
    TibiaWiki de cada item, cantidad y un buscador para saber en qué char y dónde está un item.
  Un perfil se **comparte** con un código de texto (Copiar código → pegarlo en un chat) o un archivo
  `.alfahub`; quien lo recibe lo abre en Perfiles → Importar, y si importa una versión nueva del mismo
  perfil se actualiza su copia. Las listas de criaturas e items salen de TibiaWiki con
  `assets/make_profiles_data.py` (`bestiary.json`, `items.json`, `charms.json`); vienen dentro de la app y
  no se actualizan solas: hay que volver a ejecutar el script cuando TibiaWiki añade criaturas.
- Sin límite de timers, habilidades ni espejos.
- **Hub**: barra lateral con Hoy, Timers, Tibia Audio, Espejos, Perfiles, Atajos y Sonido (ver [docs/DISENO-REFERENCIA.md](docs/DISENO-REFERENCIA.md)).

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
Los ajustes se guardan en `%APPDATA%\AlfaHub\ajustes.ini`; los timers en `timers.json`, los espejos
en `espejos.json` y Tibia Audio en `audio.json`.

Si el overlay no se ve encima de Tibia, pon Tibia en modo ventana o pantalla completa sin bordes
(una pantalla completa exclusiva no deja dibujar ventanas encima).

### BattlEye

Alfa Hub es un programa aparte que **no toca el cliente de Tibia**:

- no abre el proceso de Tibia, no lee ni escribe su memoria, no inyecta nada y no instala hooks;
- no envía teclas ni clics al juego (no es un macro ni un bot);
- para saber si Tibia está al frente solo mira el nombre del proceso y el título de la ventana
  activa, igual que el Administrador de tareas;
- los atajos son atajos globales normales de Windows (`RegisterHotKey`);
- **Tibia Audio** no instala ganchos de teclado: con Tibia al frente pregunta cada 15 ms si están
  pulsadas las teclas de tu lista (`GetAsyncKeyState`, solo lectura). No bloquea la tecla, no envía nada
  y no guarda lo que escribes;
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

**Tibia Audio** necesita el permiso de **Monitorización de entrada** (el mismo que piden Discord u OBS
para sus atajos en segundo plano): es una escucha de solo lectura del teclado, que no bloquea la tecla
ni la cambia. Alfa Hub solo la usa con Tibia Audio activado y Tibia al frente, y solo compara cada
pulsación con las teclas de tu lista. Los atajos de Alfa Hub (⌃⌥P…) no sirven para esto porque se
quedan con la tecla y Tibia no la recibiría.

Atajos por defecto: **⌃⌥P** (timer Pez), **⌃⌥R** (reiniciar el último timer usado), **⌃⌥O** (overlay).

## Ícono y personaje

El personaje sale del [Outfiter de TibiaWiki](https://tibia.fandom.com/wiki/Outfiter?o=1&c1=95&c2=113&c3=39&c4=115)
(`assets/outfit64.png`). `swift assets/make_icons.swift` lo recorta, lo centra y genera
`macos/AppIcon.icns`, `windows/AlfaHub.ico` y el sprite del hub. El ícono no lleva fondo, igual que
el de Tibia: en macOS el sistema le pone la misma placa gris.

## Compilar

- macOS: `macos/build.sh` (requiere Xcode).
- Windows: `dotnet build -c Release` dentro de `windows/` (funciona también desde macOS/Linux con el SDK de .NET).
- El workflow *Capturas Windows* compila el `.exe` en un Windows real, lo abre, recorre el hub y sube
  capturas como artefacto (se lanza solo al cambiar `windows/` o `ci/`).
