# Diseño de referencia (versión de macOS)

La versión de **macOS es la referencia visual**. La de Windows debe verse lo más parecida posible:
misma estructura, mismos colores, mismos tamaños y mismo comportamiento. Las capturas están en
[`docs/capturas/`](capturas/) (tomadas en macOS a 2×, así que **1 pt = 2 px** en las imágenes).

> **Para la sesión de Claude en Windows:** la UI de Windows (`windows/HubForm.cs`) usa hoy controles
> estándar de WinForms y se ve distinta. El objetivo es que se parezca a estas capturas. WinForms no trae
> interruptores, tarjetas redondeadas ni pestañas segmentadas, así que hay que **dibujarlos a mano**
> (controles propios con `OnPaint`), igual que ya se hace con `HeroPanel`, `MascotPanel` y `PreviewPanel`.
> Trabaja en una rama nueva y abre un PR. Compila con `dotnet build -c Release` en `windows/`.
> No cambies la lógica del timer, del overlay ni de los atajos: solo el aspecto del hub.

## Capturas

| Captura | Qué muestra |
| --- | --- |
| [01-hub-ciclo-y-sonido.png](capturas/01-hub-ciclo-y-sonido.png) | Hub completo, pestaña **Ciclo y sonido**, timer en pausa |
| [02-hub-overlay.png](capturas/02-hub-overlay.png) | Pestaña **Overlay**: vista previa y sección "En Tibia" |
| [03-hub-overlay-colores.png](capturas/03-hub-overlay-colores.png) | Pestaña **Overlay** desplazada: sección "Colores" |
| [04-hub-atajos.png](capturas/04-hub-atajos.png) | Pestaña **Atajos** (en Windows se muestran como `Ctrl+Alt+P`, etc.) |
| [05-tarjeta-en-marcha.png](capturas/05-tarjeta-en-marcha.png) | Tarjeta del timer **en marcha** (botón Pausar ámbar, estado verde) |
| [06-tarjeta-alerta.png](capturas/06-tarjeta-alerta.png) | Tarjeta del timer en **alerta** (últimos 25 s: rojo y parpadeo) |
| [07-overlay-alerta-sobre-tibia.png](capturas/07-overlay-alerta-sobre-tibia.png) | El **overlay** en alerta, encima de Tibia |
| [08-overlay-en-contexto.png](capturas/08-overlay-en-contexto.png) | El overlay en su sitio dentro de la ventana de Tibia |

## Estructura del hub (de arriba abajo)

Ventana de **420 pt de ancho**, márgenes laterales de 16 pt, margen superior de 12 pt,
separación vertical de 12 pt entre bloques. Sin fondos de color: todo en grises oscuros neutros.

1. **Franja del personaje** (alineada a la derecha, sin fondo, **sin sombra** bajo los pies)
   - Sprite `character.png` a **2×**, pixel-art sin suavizado (nearest neighbor).
   - A su izquierda, a 6 pt, una **nube de diálogo** blanca con el texto **"Slayer macaco e mielda"**,
     subida 8 pt para quedar a la altura de la cabeza.
2. **Tarjeta del timer** (padding 16 pt, esquinas de 14 pt, borde fino)
   - Fila superior: **chip de estado** a la izquierda y `Ciclo N · Total mm:ss` a la derecha.
   - Contador `mm:ss` **centrado**.
   - Barra de progreso del ciclo (5 pt de alto, extremos redondeados).
   - Botones: **Iniciar/Pausar** (ocupa el resto del ancho) y **Reiniciar** (a la derecha), 10 pt de separación.
3. **Pestañas segmentadas**: "Ciclo y sonido" · "Overlay" · "Atajos" (la seleccionada en azul).
4. **Contenido de la pestaña**, con scroll si no cabe: secciones con título y filas agrupadas en
   tarjetas redondeadas (estilo "formulario agrupado"), separadores finos entre filas.

## Colores

| Uso | Color |
| --- | --- |
| Fondo de la ventana | `#323030` (gris muy oscuro) |
| Tarjeta del timer | `#413E3E` (blanco al 5 % sobre el fondo), borde blanco al 8 % |
| Filas de las secciones | `#3B3A39` |
| Texto principal | blanco |
| Texto secundario (`Ciclo 1 · Total`, notas) | blanco al 55 % |
| **Contador del hub** y su barra de progreso | **`#EDEDED` (blanco hueso)** |
| Pista de la barra de progreso | blanco al 12 % |
| **Azul** (Iniciar, pestaña seleccionada, duración elegida, interruptores activados, deslizadores) | **`#0A84FF`** |
| Ámbar (botón Pausar) | `#D97805` |
| Alerta (contador y barra en los últimos segundos, chip "¡Alerta!") | color de alerta del usuario, por defecto `#F54D4D` |
| Chip "En marcha" | verde `#30D158` |
| Chip "En pausa" | blanco al 55 % |
| Botón Reiniciar | fondo blanco al 6 %, borde blanco al 10 % |
| Nube de diálogo | fondo `#F5F5F5`, texto `#1A1A1A`, sombra negra al 35 % (3 pt de desenfoque, 1 pt hacia abajo) |

El **color del contador del overlay** es otra cosa: lo elige el usuario y por defecto es morado
`#A873FA`. **No** se usa en el hub.

## Tipografía y tamaños

| Elemento | Fuente |
| --- | --- |
| Contador del hub | monoespaciada **negrita 56 pt** (en Windows: Cascadia Mono / Consolas Bold) |
| `Ciclo N · Total mm:ss` | monoespaciada 11 pt |
| Chip de estado | 11 pt seminegrita; punto de 7 pt; padding 8×3 pt; cápsula con el color al 15 % |
| Botones Iniciar / Reiniciar | 14 pt seminegrita, padding vertical 9 pt, esquinas de 10 pt, ícono ▶ / ❚❚ / ↺ delante |
| Pestañas segmentadas | 12–13 pt; la seleccionada con fondo azul y texto blanco |
| Títulos de sección ("Tiempo entre ciclos", "Avisos"…) | 13 pt seminegrita |
| Filas | 13 pt |
| Notas al pie de sección | 11 pt, blanco al 55 % |
| Nube de diálogo | 12,5 pt negrita redondeada (en Windows: Segoe UI Bold); padding 12 pt a la izquierda, 7 pt arriba/abajo; punta de 9 pt hacia el personaje |
| Botones de duración (01:00…03:00) | monoespaciada 12 pt seminegrita, cápsulas con padding 9×5 pt; elegida en azul, resto blanco al 8 % |

## Controles a imitar en Windows

| macOS | Equivalente a dibujar en Windows |
| --- | --- |
| Interruptor (toggle) azul | `ToggleSwitch` propio: pastilla de 38×22 pt, azul `#0A84FF` activado / gris `#5A5A5E` desactivado, círculo blanco |
| Deslizador | Pista fina gris con tramo azul hasta el pulgar y pulgar blanco redondo |
| Control segmentado | Tres botones planos dentro de una cápsula gris; el seleccionado con fondo azul |
| Formulario agrupado | Panel con esquinas de 10 pt, fondo `#3B3A39`, filas de ~36 pt con separador de 1 px |
| Stepper (▲▼) | Campo numérico oscuro sin borde claro, alineado a la derecha |
| Selector de color | Botón cápsula de 36×20 pt relleno con el color |

## Overlay (sobre Tibia)

- Solo el contador `mm:ss`, monoespaciado negrita (28 pt × tamaño), con fondo redondeado
  (esquinas 9 pt × tamaño) en el color de fondo elegido (por defecto `#111827` al 85 %).
- En alerta: color de alerta, borde fino del mismo color y parpadeo suave (opacidad entre 55 % y 100 %, ~1 s).
- En pausa: el número al 60 % de opacidad.
- Con el fondo transparente: contorno negro alrededor de los números para que se lean sobre el juego.
- Desbloqueado para moverlo: borde ámbar discontinuo.
