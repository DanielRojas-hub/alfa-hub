# Pruebas de Alfa Hub en Windows

Lista de funcionalidades para probar `AlfaHub.exe` (última
[Release](https://github.com/DanielRojas-hub/alfa-hub/releases/latest)) en un PC con Windows 10/11.

Lo que ya está verificado sin Tibia, en un Windows real (workflow *Capturas Windows* de GitHub Actions):
el hub y sus páginas, iniciar/pausar timers, el editor, dos timers a la vez, la alerta, el overlay
desbloqueado con varios timers, y los espejos (con el Bloc de notas en lugar de Tibia): elegir la región,
copia en vivo, arrastrar, bloquear y ocultarse cuando la ventana de origen no está al frente. **Falta todo lo que necesita Tibia** (secciones 5, 6, 7 y 9) y el escalado
de pantalla distinto de 100 %.

> **Para la sesión de Claude en Windows:** guía al usuario punto por punto, pídele capturas cuando algo
> se vea mal y anota el resultado de cada punto en `RESULTADOS-WINDOWS.md` (✅ / ❌ + qué pasó).
> Si corriges código, hazlo en una rama nueva y abre un PR; no subas directamente a `main`.
> El código de Windows está en `windows/` y se compila con `dotnet build -c Release` dentro de esa carpeta
> (requiere el SDK de .NET; apunta a .NET Framework 4.8). Referencia visual:
> [docs/DISENO-REFERENCIA.md](docs/DISENO-REFERENCIA.md).
>
> **No pruebes nada que toque el cliente de Tibia** (abrir su proceso, leer memoria, enviar teclas):
> la app está diseñada para no hacerlo por BattlEye.

## 1. Arranque

- [ ] Al abrir `AlfaHub.exe`, si aparece "Windows protegió su PC", **Más información → Ejecutar de todas formas** funciona.
- [ ] El `.exe` muestra el ícono del personaje (sin fondo) en el Explorador.
- [ ] Se abre el hub con la barra de título oscura, la barra superior (sin logo) y la barra lateral.
- [ ] Aparece el ícono del personaje en la bandeja del sistema (junto al reloj).
- [ ] Abrir el `.exe` por segunda vez muestra "Alfa Hub ya está abierto" y no abre otra copia.
- [ ] Si venías de una versión anterior, el timer **Pez** conserva tu duración y tu atajo, y aparecen también **Bakragore** y **Pilar**.

## 2. Aspecto del hub

- [ ] Se parece a las capturas de [docs/capturas/](docs/capturas/) (macOS).
- [ ] Barra superior: "● Tibia abierto" en verde con Tibia abierto y "Tibia cerrado" en gris sin él.
- [ ] Barra lateral: Timers (con un contador verde de timers en marcha), Tibia Audio, Espejos, Atajos, Sonido; personaje y nube al pie.
- [ ] Con el escalado de Windows al 125 % y al 150 %, nada se corta ni se solapa.

## 3. Timers

- [ ] **＋ Añadir timer** ofrece Pez, Bakragore, Pilar y Personalizado, y abre el editor del nuevo (sin límite).
- [ ] En la lista, ▶ inicia y ❚❚ pausa cada timer por separado; ↺ lo reinicia; la barra de color de cada fila cambia (verde, ámbar, rojo).
- [ ] Varios timers pueden ir a la vez y "N en marcha" se actualiza.
- [ ] **Pausar todos** y **Reiniciar todos** funcionan.
- [ ] Menú ⋮: **Duplicar** crea una copia (sin atajo) y **Eliminar** la quita (no deja borrar el último).
- [ ] **Pez**: cuenta atrás de 2:00 que se repite; rojo y parpadeo en los últimos 25 s; beeps en los últimos 5 s; sonido de aviso al empezar cada ciclo.
- [ ] **Bakragore**: mecánica cada 1:30; el detalle dice "Quedan mm:ss" y a los 25 min se detiene con "Terminado".
- [ ] **Pilar**: flecha y segundos (`↑ 6` → `→ 6` → `↓ 6` → `← 6`), sonido al cambiar de dirección y beeps en los últimos 2 s.
- [ ] **Personalizado**: cuenta atrás de 1:00 que no se repite y termina con el sonido de aviso.
- [ ] El tiempo no se desfasa: tras ~5 min con el hub minimizado, "Total" coincide con un cronómetro.

## 4. Editor de un timer

- [ ] **Editar** abre el detalle; "‹ Volver a Timers" vuelve a la lista.
- [ ] Cambiar el **nombre** (Enter o salir del campo) se refleja en la lista, el overlay y Atajos.
- [ ] **Tipo** Cuenta atrás / Rotación cambia las secciones del editor.
- [ ] **Cargar preset** cambia la configuración y conserva el sonido y el atajo.
- [ ] Duración: botones 01:00…03:00, personalizado (min / s, mínimo 5 s), repetir y duración total.
- [ ] Rotación: segundos por dirección, dirección inicial y beeps.
- [ ] **Sonido de aviso**: el menú tiene 14 sonidos y cada uno suena al elegirlo; **Archivo propio…** acepta un `.wav` y un `.mp3`; **Probar** lo reproduce con el volumen de la app.
- [ ] Si el archivo de sonido se borra o mueve, suena el beep en su lugar.
- [ ] **Atajo iniciar / pausar** de cada timer funciona con Tibia al frente.
- [ ] **Eliminar este timer** lo borra y vuelve a la lista.

## 5. Overlay (lo más importante)

Probar con Tibia abierto, primero en **modo ventana** y luego en **pantalla completa**.

- [ ] El overlay aparece **solo cuando Tibia está al frente**; al cambiar de ventana desaparece y al volver reaparece.
- [ ] Con un solo timer en marcha muestra solo su contador; con varios, uno debajo de otro y con su nombre.
- [ ] Se ve **encima de Tibia** en modo ventana y en pantalla completa (si no, anotar el modo de pantalla de Tibia).
- [ ] Los **clics atraviesan** el overlay y llegan a Tibia.
- [ ] El overlay **no roba el foco**: se puede seguir escribiendo/jugando en Tibia.
- [ ] Cada timer tiene su propio contador; sin sitio propio se apilan arriba a la derecha.
- [ ] Timers → "Desbloquear para moverlos": se ven todos con borde ámbar, cada uno se arrastra por separado y al bloquear los clics vuelven a atravesarlos; los sitios se conservan al reiniciar la app.
- [ ] En el editor de cada timer, sección **Overlay**: mostrar en el overlay, tamaño, opacidad, colores (contador, alerta, fondo y su opacidad; a 0 deja solo el número con contorno), "Restaurar colores" y "Volver a su sitio automático".
- [ ] Botón "Overlay visible / oculto" de la barra superior y atajo Ctrl+Alt+O.

## 5b. Tibia Audio

- [ ] **＋ Añadir habilidad** abre su editor; "Tecla en Tibia" acepta una tecla sola (p. ej. **1** o **F1**) o con modificador.
- [ ] Con **Activar Tibia Audio** encendido y Tibia al frente, pulsar la tecla en el juego **usa la habilidad en Tibia** (la tecla llega al juego) y empieza el cooldown en la fila.
- [ ] Al terminar el cooldown suena el sonido elegido con su volumen.
- [ ] "Si la pulsas durante el cooldown": *Vuelve a empezar* reinicia la cuenta; *Se ignora* no.
- [ ] Con otra ventana al frente (no Tibia), pulsar la tecla no hace nada.
- [ ] Si la tecla también es un atajo de Alfa Hub, el editor lo avisa.

## 6. Espejos

- [ ] **Espejos → ＋ Añadir espejo** con Tibia abierto: el hub se oculta, Tibia se oscurece y se puede arrastrar la región (Esc cancela).
- [ ] Sin Tibia abierto avisa "Abre Tibia…".
- [ ] El espejo muestra **en vivo** la región elegida (cooldowns, hechizos, barra de vida…), nítido al ampliarlo.
- [ ] El espejo nuevo aparece desbloqueado (borde ámbar) y se arrastra a su sitio; "Bloquear" lo fija y deja pasar los clics a Tibia.
- [ ] Bloqueado, solo se ve con **Tibia al frente**; al cambiar de ventana desaparece.
- [ ] Funciona con Tibia en **modo ventana** y en **pantalla completa sin bordes** (en pantalla completa exclusiva Windows no puede componer la miniatura).
- [ ] Visible, opacidad, tamaño, borde, volver a elegir la región, eliminar, mostrar/ocultar todos.
- [ ] Si Tibia cambia de tamaño o se reinicia, los espejos siguen funcionando (vuelven a engancharse solos).
- [ ] Con varios espejos y timers a la vez el juego no pierde FPS de forma notable.

## 6b. Perfiles

Ya verificado sin pantalla (con `ALFAHUB_TEST_SHOTS`, ver más abajo): sincronizar Alfa Sios con tibia.com,
las cinco pestañas, la ficha de una criatura, el editor de un item, el outfit del char en el ícono y
compartir/importar un código (también un código hecho en macOS, y al revés).

- [ ] **Perfiles → ＋ Nuevo perfil**, abrir el char y escribir el nombre de un char real: a los ~2 s se
      rellena (vocación, mundo, gremio) con "✓ tibia.com ahora mismo"; un nombre que no existe muestra el
      aviso ámbar y **Reintentar**.
- [ ] Pestañas **General**, **Experiencia** (el primer día, "Primer registro"), **Bestiary** (Todas,
      Single, Group, filtros y orden; clic en una criatura abre su ficha y cambiar las kills se guarda),
      **Charms** (clic cambia el nivel; optimizador con **Toda una zona**) e **Inventario** (añadir con
      **＋**, mover a otro sitio, equipar, quitar, buscar en todos los chars).
- [ ] Clic en el outfit del char → editor; **Mostrar en el ícono de Alfa Hub** cambia el ícono de la
      ventana, la bandeja y el personaje de la barra lateral.
- [ ] **Compartir → Copiar código** y **Importar → Pegar código** crean una copia "COMPARTIDO"; importar
      otra vez el mismo perfil la actualiza. **Guardar archivo .alfahub** y **Abrir archivo** también.
- [ ] Un código copiado en la versión de macOS se importa aquí con sus chars, items, kills y charms.
- [ ] Tras "Salir" y volver a abrir se conservan los perfiles (`%APPDATA%\AlfaHub\perfiles.json`).

Prueba automática sin pantalla: `ALFAHUB_TEST_SHOTS=C:\capturas AlfaHub.exe` recorre Perfiles solo,
guarda una captura de cada pantalla en esa carpeta, anota los errores en `errores.txt` y se cierra.

## 7. Atajos

- [ ] Página Atajos: un atajo por timer y los globales (Ctrl+Alt+R reinicia el último timer usado, Ctrl+Alt+O el overlay).
- [ ] Con **Tibia al frente**, cada atajo funciona y no escribe nada en el chat de Tibia.
- [ ] Grabar un atajo nuevo (p. ej. **F8**); **Esc** cancela; una tecla sin modificador se rechaza.
- [ ] Una combinación solo puede estar en un sitio: asignarla a otro timer la quita del anterior.
- [ ] Si otro programa ya usa la combinación, aparece un aviso en la bandeja.

## 8. Sonido, bandeja y ajustes guardados

- [ ] Página Sonido: Silenciar, Volumen (con %) y Probar beep.
- [ ] Cerrar el hub con la X lo oculta; los timers y el overlay siguen. Doble clic en la bandeja lo abre.
- [ ] Menú de la bandeja: Abrir, Iniciar/pausar y Reiniciar el timer elegido, Salir.
- [ ] Tras "Salir" y volver a abrir se conservan timers, sonidos, atajos, colores y posición del overlay
      (`%APPDATA%\AlfaHub\timers.json`, `espejos.json` y `ajustes.ini`).

## 9. BattlEye

- [ ] Jugar una sesión normal (30+ min) con Alfa Hub abierto, varios timers, espejos y el overlay visible: sin avisos, expulsiones ni errores de BattlEye.

## Qué devolver

Un resumen con ✅ / ❌ por sección, capturas de lo que se vea mal, la versión de Windows, el escalado de
pantalla (100/125/150 %) y el modo de pantalla de Tibia (ventana / pantalla completa).
