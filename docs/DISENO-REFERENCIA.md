# Diseño de referencia

Las dos versiones (macOS y Windows) comparten diseño: ventana ancha con **barra superior**,
**barra lateral** y una **página** a la derecha, inspirada en TibiaVision. Las capturas de
[`docs/capturas/`](capturas/) son de la versión de macOS (a 2×: **1 pt = 2 px**). La de Windows se
verifica con el workflow *Capturas Windows* (GitHub Actions), que sube sus capturas como artefacto.

## Capturas (macOS)

| Captura | Qué muestra |
| --- | --- |
| [01-timers.png](capturas/01-timers.png) | Página **Timers**: lista de timers en pausa |
| [02-editor-cuenta-atras.png](capturas/02-editor-cuenta-atras.png) | Editor de un timer de **cuenta atrás** (Pez) |
| [03-editor-rotacion.png](capturas/03-editor-rotacion.png) | Editor de un timer de **rotación** (Pilar) |
| [04-editor-overlay.png](capturas/04-editor-overlay.png) | Sección **Overlay** del editor de un timer: vista previa, tamaño, opacidad y colores |
| [05-atajos.png](capturas/05-atajos.png) | Página **Atajos**: uno por timer y los globales |
| [06-sonido.png](capturas/06-sonido.png) | Página **Sonido** |
| [07-timers-en-marcha.png](capturas/07-timers-en-marcha.png) | Lista con dos timers en marcha |
| [08-overlay-sobre-tibia.png](capturas/08-overlay-sobre-tibia.png) | El **overlay** con dos timers apilados, encima de Tibia |
| [09-espejos.png](capturas/09-espejos.png) | Página **Espejos** (macOS, sin el permiso de grabación todavía) |
| [10-espejo-windows.png](capturas/10-espejo-windows.png) | Un **espejo** en Windows (abajo a la derecha) copiando parte de otra ventana, con el overlay de timers |
| [11-tibia-audio.png](capturas/11-tibia-audio.png) | Página **Tibia Audio** con una habilidad en cooldown |
| [12-editor-tibia-audio.png](capturas/12-editor-tibia-audio.png) | Editor de una habilidad de Tibia Audio |
| [13-outfit.png](capturas/13-outfit.png) | Página **Outfit** (Outfiter) |
| [14-hoy.png](capturas/14-hoy.png) | Página **Hoy**: Rashid, Tibiadrome, server save y boosteados |
| [15-perfiles-inventario.png](capturas/15-perfiles-inventario.png) | **Perfiles**: inventario de un char (equipo y un contenedor por sitio) |
| [16-perfiles-bestiary.png](capturas/16-perfiles-bestiary.png) | **Perfiles**: Bestiary de un char (kills, etapas y completas) |
| [17-perfiles-general.png](capturas/17-perfiles-general.png) | **Perfiles**: pestaña General de un char (datos de tibia.com) |
| [18-perfiles-experiencia.png](capturas/18-perfiles-experiencia.png) | **Perfiles**: pestaña Experiencia el primer día (un solo registro) |
| [19-perfiles-charms.png](capturas/19-perfiles-charms.png) | **Perfiles**: optimizador de charms |
| [20-perfiles-outfit.png](capturas/20-perfiles-outfit.png) | **Perfiles**: editor de outfit de un char con "Mostrar en el ícono de Alfa Hub" |

## Estructura

Ventana de ~900 × 640 pt (mínimo 820 × 600).

1. **Barra superior** (52 pt), sin logo ni nombre: a la derecha, el estado "● Tibia abierto / cerrado"
   y un botón cápsula "Overlay visible / oculto".
2. **Barra lateral** (200 pt, fondo más oscuro):
   - Sección **PRINCIPAL**: *Hoy*, *Timers* (con un contador verde de timers en marcha), *Tibia Audio*, *Espejos* y *Perfiles*.
   - Sección **AJUSTES**: *Outfit* (solo Windows; en macOS el outfit es de cada char de Perfiles), *Atajos* y *Sonido*.
   - Elemento elegido: fondo gris suave y marca azul vertical a la izquierda.
   - Al pie: el personaje guardado a 2× (caminando si se eligió **Animar caminata**); en macOS, el del char
     que tiene **Mostrar en el ícono de Alfa Hub**.
3. **Página** (márgenes de 28 pt): título grande + subtítulo gris, y el contenido.

### Página Hoy

- Cuatro tarjetas (2 × 2) con una franja de color arriba, ícono, título y el sprite de Tibia a la derecha
  (`assets/hoy/`, de TibiaWiki):
  - **Rashid** (dorado): ciudad en grande, dónde está y "Mañana: …". Cambia en el server save según
    el día (lunes Svargrond, martes Liberty Bay, miércoles Port Hope, jueves Ankrahmun, viernes
    Darashia, sábado Edron, domingo Carlin).
  - **Tibiadrome** (morado): lo que falta ("7 d 12 h"), barra de la rotación de 2 semanas y fecha de fin en hora local.
  - **Server save** (azul; rojo en los últimos 15 min): cuenta atrás `hh:mm:ss`, barra del día y la hora local.
  - **Boosteados** (naranja): GIF, nombre y "CRIATURA" / "BOSS" (TibiaData, `/v4/creatures` y
    `/v4/boostablebosses`). Se vuelven a pedir tras el server save.

### Página Perfiles (solo macOS por ahora)

- Lista de perfiles (filas con los outfits de sus primeros chars, "2 chars · 10 items · …", chip
  COMPARTIDO si se importó), **Importar** (pegar código o abrir archivo `.alfahub`) y **＋ Nuevo perfil**.
- Un perfil: nombre editable, **Compartir** (copiar código / guardar archivo), **＋ Añadir char** y una
  tarjeta por char (outfit, vocación · nivel · mundo, items y progreso del Bestiary).
- Un char: "‹ Volver a <perfil>" y una cabecera en una línea: outfit 68 pt con la franja "✎ Outfit" abajo
  ("Editar outfit" y borde azul al pasar el ratón; clic abre el editor) y, en su esquina, un botón redondo
  para **mostrarlo en el ícono de Alfa Hub** (azul cuando lo está; chip ÍCONO junto al nombre), nombre grande editable y debajo
  "Elder Druid · Xymera · Inferno ✓ tibia.com hace 2 minutos" (clic para volver a sincronizar); a la
  derecha, tres recuadros: NIVEL (verde), BESTIARY (ámbar) y CHARM POINTS (morado). Al dejar de escribir
  el nombre (1,2 s) se busca en tibia.com; si no existe, vocación, nivel y mundo a mano y un aviso ámbar
  con Reintentar. Se vuelve a sincronizar al abrirlo si pasó una hora y, sola, una vez por día de Tibia.
- Pestañas con ícono y raya azul debajo de la elegida (como guildstats): **General**, **Experiencia**,
  **Bestiary**, **Charms** e **Inventario**. Las opciones dentro de una pestaña van en cápsulas (Todas /
  Single / Group, Experiencia / Nivel) y las explicaciones, en un botón ⓘ.
  - Editor de outfit ("‹ Volver a <char>"): el Outfiter (listas de outfits y monturas, vista previa,
    Hombre/Mujer, addons, montura, caminata, colores y *Mis outfits*); los cambios se guardan solos en el
    char. Arriba a la derecha, el interruptor **Mostrar en el ícono de Alfa Hub** (Dock, barra de menús y
    barra lateral; solo un char a la vez).
  - General: franja con un degradado verde: NIVEL grande, "#11 EN XYMERA", barra hasta el siguiente nivel
    ("57 % · faltan 47,2M exp para el 1.487") y, separados por una raya, HP, maná, capacidad y party con
    su ícono en un círculo y una barra de color (fórmulas de TibiaWiki). Debajo, dos columnas: a la
    izquierda la ficha **Personaje** (filas con ícono: vocación, sexo, mundo, residencia, título, puntos de
    logro, cuenta, última conexión, cuenta creada, lealtad, casado con) y **Muertes recientes** (nivel en
    rojo y el texto); a la derecha **Gremio**, **Casas**, **Otros chars de la cuenta** (con su número;
    botón azul ＋ para añadirlo al perfil o ✓ si ya está), **Antes** y **Comentario**. Al pie, de dónde
    salen los datos.
  - Experiencia: cuatro recuadros (Mejor día, Media diaria de los últimos 7 días, Puesto en <mundo> con
    lo que subió o bajó, % al siguiente nivel); gráfico **Evolución** (Experiencia / Nivel, escala de los
    datos), barras **Experiencia por día** (el mejor día en ámbar), **Predicción de nivel** (nivel
    objetivo, +10 / +50 / +100, fecha estimada con la media) y tabla Día · Exp ganada · Puesto · Nivel
    (+n) · Experiencia. El primer día (un solo registro) salen los recuadros con lo que se sabe hoy y una
    tarjeta "Primer registro: <fecha>" con su nivel, experiencia y puesto, que explica que mañana empieza
    el historial. Los días anteriores no se traen: guildstats.eu protege su historial contra programas.
  - Bestiary: Todas / Single / Group, buscador y menús de clase y dificultad (con completas/total),
    estado y orden (clase, nombre, charm points, progreso, HP, kills que faltan). Todas: resumen
    (completas, %, en curso, sin empezar, charm points) y cuadrícula de criaturas por clase (cabecera
    "Amphibic 0/11 · 0 / 190"): GIF, nombre, barra y kills; borde verde si está completa, azul si está en
    curso; punto ámbar si es muy rara. Clic: ficha (etapas, kills con +1/+10/+100, marcar completa, daño
    que recibe de cada elemento y dónde vive).
    - Single: hasta 40 filas con puesto, GIF, nombre y etiquetas (FALTAN …, FÁCIL/NORMAL/DIFÍCIL, HP,
      +CP), por charm points × en cuántos sitios vive ÷ (golpes para matarla al nivel del char ×
      √kills que faltan); sin muy raras ni las que tienen más de 60 HP por nivel.
    - Group: zonas con 2 o más criaturas sin terminar, por charm points; etiquetas (por terminar, +CP,
      dificultad 1–10) y sus GIFs con las kills que faltan; clic para ver su cuadrícula.
  - Charms: **Optimizador** / **Mis charms** (cápsulas; ⓘ explica las fórmulas).
    - Optimizador (como hakaimarket): tarjeta de configuración con *Personaje* (nivel, vocación, HP y
      maná), *Tus charms* (los 9 de daño: imagen, nombre y nivel; clic cambia No → I → II → III) y *Tu hunt*
      (buscador que añade criaturas al pulsarlas o con Enter, **Toda una zona**, **Vaciar** y una tarjeta
      por criatura: GIF, nombre, ⚠ si su Bestiary no está completo, HP y a qué es débil, y − kills/hora +).
      Debajo, **Mejor combinación** (borde ámbar, chip BEST): una tarjeta por charm con charm → criatura,
      el daño por activación en grande y la probabilidad; "Ver el daño de cada charm en cada criatura"
      despliega la tabla.
    - Mis charms: recuadros de charm points (del Bestiary, gastados en mayores y en menores) y la
      cuadrícula de charms mayores y menores con su imagen de TibiaWiki y tres rayas del nivel (clic:
      efecto y elegir No / I / II / III con su coste).
  - Inventario: buscador de items en todos los chars del perfil; a la izquierda **Equipado** con los
    huecos como en Tibia y, en dos columnas, un contenedor por sitio (Mochila, Depot, Casa, Stash, Otro
    char, Otro sitio) con casillas de 44 pt agrupadas por detalle y una casilla **＋**. Clic en un item:
    cambiarlo, cantidad, moverlo (equiparlo lo pone en su hueco y lo que hubiera pasa a la mochila),
    detalle y quitar.
- Formato compartido: JSON `{"alfaHubProfile": 1, "profile": {…}}`; el código es `ALFAHUB1:` + base64
  del JSON comprimido con DEFLATE sin cabecera (en .NET, `DeflateStream`).

### Página Timers

- Cabecera con el botón azul **＋ Añadir timer** (menú: Pez, Bakragore, Pilar, Personalizado).
- Línea "3 timers · 1 en marcha" y botones **Desbloquear para moverlos** (contadores del overlay),
  **Pausar todos** / **Reiniciar todos**. Sin límite de timers.
- Una **fila por timer** (tarjeta redondeada): barra de estado de 4 pt a la izquierda
  (gris parado, verde en marcha, ámbar en pausa, rojo en alerta o terminado), nombre en negrita y
  resumen gris ("Cada 02:00 · F7 · Beep"), contador monoespaciado con barra de progreso, botones
  redondos ▶/❚❚ (azul/ámbar) y ↺, botón **Editar** y menú ⋮ (Duplicar, Eliminar).

### Editor de un timer

- "‹ Volver a Timers" en azul.
- Tarjeta con nombre, chip de estado, detalle, botones Iniciar/Reiniciar y contador grande a la derecha.
- Secciones: **Timer** (nombre, tipo *Cuenta atrás / Rotación*, cargar preset), **Duración**
  (botones 01:00…03:00, personalizado, repetir, duración total) o **Rotación** (segundos por
  dirección, dirección inicial, beeps), **Avisos**, **Sonido y atajo** (sonido propio o beep,
  probar, atajo iniciar/pausar), **Overlay** (mostrar en el overlay, vista previa, ver en alerta,
  tamaño, opacidad, colores del contador, de la alerta y del fondo, restaurar colores, volver a su
  sitio automático) y **Eliminar este timer**.
- Sonidos incluidos (iguales en las dos versiones): Beep, Doble beep, Triple beep, Ding, Campana,
  Carillón, Jingle, Moneda, Alarma, Sirena, Burbuja, Descenso, Gong y Tic; o un archivo propio.

### Página Tibia Audio

- Cabecera con **＋ Añadir habilidad** (abre su editor).
- Interruptor **Activar Tibia Audio** (apagado por defecto), estado ("1 habilidad · Escuchando con
  Tibia al frente") y **Probar todas**. En macOS, aviso ámbar si falta el permiso de Monitorización de entrada.
- Una fila por habilidad: barra verde (lista) o ámbar (en cooldown), nombre, "1 · 2 s · Ding", segundos
  que faltan ("1,5 s" en ámbar o "Lista") con barra, interruptor, volumen, **Editar** y ⋮ (Probar, Duplicar, Eliminar).
- Editor: nombre, activada, **tecla en Tibia** (cualquier tecla, también sin modificador), cooldown
  (segundos, pasos de 0,5), qué pasa si se pulsa durante el cooldown (vuelve a empezar / se ignora),
  sonido, volumen, simular pulsación y eliminar.

### Página Outfit (solo Windows; en macOS es el editor de outfit de cada char)

- Cabecera con **Guardar cambios** (azul, solo con cambios) y **Descartar**.
- A la izquierda, siempre desplegadas, la lista de outfits y debajo la de monturas, cada una con
  buscador (Enter elige el que coincide). Elegir una montura la monta.
- A la derecha: vista previa sobre fondo de hierba (camina si se pide), nombre, girar ↺ ↻ y
  orientación, **Hombre / Mujer**, **Addon 1**, **Addon 2**, **Montura** (ir montado en la elegida) y
  **Animar caminata**; **Colores** (Outfit/Montura, la segunda solo montado en una montura que se puede
  colorear; Cabeza, Primario, Secundario, Detalle) con la paleta de 133 colores de Tibia (7 × 19).
- **Mis outfits**: nombre + **Guardar aquí**; casillas con miniatura (la primera, el personaje
  original). Clic carga el outfit en la vista previa; × o clic derecho lo quita.
- Lo editado es un borrador: solo pasa al ícono de la app, la barra lateral y la barra de menús /
  bandeja al pulsar **Guardar cambios**.

### Página Espejos

- Cabecera con **＋ Añadir espejo**: oculta el hub y oscurece la ventana de Tibia para arrastrar la región.
- Línea "1 espejo · 1 visibles" con **Ocultar/Mostrar todos** y **Desbloquear para moverlos / Bloquear**.
- Una fila por espejo: barra verde si está visible, nombre, "410 × 130 px · 100 % · Visible",
  interruptor de visible, opacidad, **Editar** y ⋮ (volver a elegir la región, eliminar).
- Editor: nombre, visible, tamaño (25–400 %), opacidad, borde, volver a elegir la región, eliminar.
- Ventana del espejo: sin marco, siempre encima; borde fino gris (opcional) o ámbar discontinuo
  si está desbloqueada; solo se ve con Tibia al frente (o desbloqueada) y deja pasar los clics.

## Colores

| Uso | Color |
| --- | --- |
| Contenido | `#1C1C1E` |
| Barra lateral y superior | `#151517` |
| Filas, secciones | blanco al 5 % sobre el contenido (`#28282B`) |
| Separadores | `#28282B` |
| Contadores del hub y sus barras | blanco hueso `#EDEDED` |
| **Azul** (botones principales, selección, interruptores, deslizadores) | `#0A84FF` |
| Ámbar (pausar, timer pausado) | `#D97805` |
| Verde (en marcha, Tibia abierto) | `#30D158` |
| Alerta | color de alerta del usuario (por defecto `#F54D4D`) |
| Texto secundario | blanco al 55 % |

El **contador del overlay** usa el color que elige el usuario (por defecto morado `#A873FA`).

## Overlay

- Una ventana por timer, con el aspecto de ese timer. Muestra los timers **en marcha o a medias**
  (si no hay ninguno, el elegido); los que no tienen sitio propio se apilan arriba a la derecha.
  Con más de un contador a la vista, cada uno lleva su nombre delante.
- Desbloqueado se ven todos (con su overlay activado) para arrastrar cada uno; al soltarlo guarda su sitio.
- Cuenta atrás: `mm:ss`. Rotación: flecha y segundos (`↑ 6`).
- En alerta: color de alerta y parpadeo; en pausa: al 60 %; sin fondo: contorno negro;
  desbloqueado: borde ámbar discontinuo.
