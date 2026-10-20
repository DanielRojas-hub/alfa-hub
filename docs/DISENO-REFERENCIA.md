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

## Estructura

Ventana de ~900 × 640 pt (mínimo 820 × 600).

1. **Barra superior** (52 pt), sin logo ni nombre: a la derecha, el estado "● Tibia abierto / cerrado"
   y un botón cápsula "Overlay visible / oculto".
2. **Barra lateral** (200 pt, fondo más oscuro):
   - Sección **PRINCIPAL**: *Timers* (con un contador verde de timers en marcha), *Tibia Audio* y *Espejos*.
   - Sección **AJUSTES**: *Atajos* y *Sonido*.
   - Elemento elegido: fondo gris suave y marca azul vertical a la izquierda.
   - Al pie: la **nube de diálogo** "Slayer macaco e mielda" (punta hacia abajo) y el personaje a 2×.
3. **Página** (márgenes de 28 pt): título grande + subtítulo gris, y el contenido.

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
| Nube de diálogo | `#F5F5F5`, texto `#1A1A1A` |

El **contador del overlay** usa el color que elige el usuario (por defecto morado `#A873FA`).

## Overlay

- Una ventana por timer, con el aspecto de ese timer. Muestra los timers **en marcha o a medias**
  (si no hay ninguno, el elegido); los que no tienen sitio propio se apilan arriba a la derecha.
  Con más de un contador a la vista, cada uno lleva su nombre delante.
- Desbloqueado se ven todos (con su overlay activado) para arrastrar cada uno; al soltarlo guarda su sitio.
- Cuenta atrás: `mm:ss`. Rotación: flecha y segundos (`↑ 6`).
- En alerta: color de alerta y parpadeo; en pausa: al 60 %; sin fondo: contorno negro;
  desbloqueado: borde ámbar discontinuo.
