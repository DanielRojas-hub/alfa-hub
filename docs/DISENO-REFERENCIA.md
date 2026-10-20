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
| [04-overlay.png](capturas/04-overlay.png) | Página **Overlay**: vista previa, ajustes y colores |
| [05-atajos.png](capturas/05-atajos.png) | Página **Atajos**: uno por timer y los globales |
| [06-sonido.png](capturas/06-sonido.png) | Página **Sonido** |
| [07-timers-en-marcha.png](capturas/07-timers-en-marcha.png) | Lista con dos timers en marcha |
| [08-overlay-sobre-tibia.png](capturas/08-overlay-sobre-tibia.png) | El **overlay** con dos timers apilados, encima de Tibia |
| [09-espejos.png](capturas/09-espejos.png) | Página **Espejos** (macOS, sin el permiso de grabación todavía) |
| [10-espejo-windows.png](capturas/10-espejo-windows.png) | Un **espejo** en Windows (abajo a la derecha) copiando parte de otra ventana, con el overlay de timers |

## Estructura

Ventana de ~900 × 640 pt (mínimo 820 × 600).

1. **Barra superior** (52 pt): personaje pequeño + "**Alfa**Hub" ("Alfa" en azul); a la derecha,
   el estado "● Tibia abierto / cerrado" y un botón cápsula "Overlay visible / oculto".
2. **Barra lateral** (200 pt, fondo más oscuro):
   - Sección **PRINCIPAL**: *Timers* (con un contador verde de timers en marcha), *Espejos* y *Overlay*.
   - Sección **AJUSTES**: *Atajos* y *Sonido*.
   - Elemento elegido: fondo gris suave y marca azul vertical a la izquierda.
   - Al pie: la **nube de diálogo** "Slayer macaco e mielda" (punta hacia abajo) y el personaje a 2×.
3. **Página** (márgenes de 28 pt): título grande + subtítulo gris, y el contenido.

### Página Timers

- Cabecera con el botón azul **＋ Añadir timer** (menú: Pez, Bakragore, Pilar, Personalizado).
- Línea "3 de 8 · 1 en marcha" y botones **Pausar todos** / **Reiniciar todos**.
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
  probar, atajo iniciar/pausar) y **Eliminar este timer**.

### Página Espejos

- Cabecera con **＋ Añadir espejo**: oculta el hub y oscurece la ventana de Tibia para arrastrar la región.
- Línea "1 de 10 · 1 visibles" con **Ocultar/Mostrar todos** y **Desbloquear para moverlos / Bloquear**.
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
| Nube de diálogo | `#F5F5F5`, texto `#1A1A1A` |

El **contador del overlay** usa el color que elige el usuario (por defecto morado `#A873FA`).

## Overlay

- Muestra los timers **en marcha o a medias**, uno debajo de otro y alineados a la derecha;
  si no hay ninguno, el timer elegido. Con más de un timer, cada contador lleva su nombre delante.
- Cuenta atrás: `mm:ss`. Rotación: flecha y segundos (`↑ 6`).
- En alerta: color de alerta y parpadeo; en pausa: al 60 %; sin fondo: contorno negro;
  desbloqueado: borde ámbar discontinuo.
