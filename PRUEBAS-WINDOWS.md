# Pruebas de Alfa Hub en Windows

Lista de funcionalidades para probar `AlfaHub.exe` (Release
[v1.0.0](https://github.com/DanielRojas-hub/alfa-hub/releases/tag/v1.0.0)) en un PC con Windows 10/11.
La versión de macOS ya está probada; la de Windows se compiló desde macOS y **nunca se ha ejecutado en Windows**.

> **Para la sesión de Claude en Windows:** guía al usuario punto por punto, pídele capturas cuando algo
> se vea mal y anota el resultado de cada punto en `RESULTADOS-WINDOWS.md` (✅ / ❌ + qué pasó).
> Si corriges código, hazlo en una rama nueva y abre un PR; no subas directamente a `main`.
> El código de Windows está en `windows/` y se compila con `dotnet build -c Release` dentro de esa carpeta
> (requiere el SDK de .NET; apunta a .NET Framework 4.8).
>
> **No pruebes nada que toque el cliente de Tibia** (abrir su proceso, leer memoria, enviar teclas):
> la app está diseñada para no hacerlo por BattlEye.

## 1. Arranque

- [ ] Al abrir `AlfaHub.exe`, si aparece "Windows protegió su PC", **Más información → Ejecutar de todas formas** funciona.
- [ ] El `.exe` muestra el ícono del personaje (sin fondo) en el Explorador.
- [ ] Se abre el hub con la barra de título oscura.
- [ ] Aparece el ícono del personaje en la bandeja del sistema (junto al reloj).
- [ ] Abrir el `.exe` por segunda vez muestra "Alfa Hub ya está abierto" y no abre otra copia.

## 2. Aspecto del hub

Referencia visual: [docs/DISENO-REFERENCIA.md](docs/DISENO-REFERENCIA.md) y las capturas de
[docs/capturas/](docs/capturas/) (versión de macOS). Si el hub de Windows no se parece, ajustarlo
siguiendo esa guía antes de seguir con el resto de pruebas.

- [ ] Arriba a la derecha: el personaje (pixel-art nítido, sin sombra) con una **nube de diálogo blanca** a su izquierda que dice "Slayer macaco e mielda", con la punta hacia él.
- [ ] Tarjeta gris neutra con: estado ("En pausa"), "Ciclo 1 · Total 00:00", contador **02:00 en blanco hueso** centrado, barra de progreso, botón **Iniciar azul** y botón Reiniciar gris.
- [ ] Pestañas "Ciclo y sonido", "Overlay", "Atajos": la seleccionada en azul y cambian el contenido.
- [ ] Con el escalado de Windows al 125 % y al 150 %, nada se corta ni se solapa (textos de botones, campos, nube).
- [ ] Se puede redimensionar la ventana en alto y las pestañas hacen scroll si no caben.

## 3. Timer

- [ ] **Iniciar** arranca la cuenta atrás; el botón pasa a **Pausar** (ámbar) y el estado a "En marcha" (verde).
- [ ] **Pausar** detiene; al reanudar sigue desde donde estaba.
- [ ] **Reiniciar** vuelve a 02:00 y a "Ciclo 1 · Total 00:00".
- [ ] En los **últimos 25 s** el contador se pone rojo, parpadea y el estado dice "¡Alerta!".
- [ ] Suenan **beeps** en los últimos 5 s de cada ciclo.
- [ ] Suena un **tono** al empezar cada ciclo nuevo y el contador de ciclos aumenta.
- [ ] El tiempo no se desfasa: tras ~5 min con el hub minimizado, "Total" coincide con un cronómetro.

## 4. Pestaña "Ciclo y sonido"

- [ ] Los botones 01:00 / 01:30 / 02:00 / 02:30 / 03:00 cambian la duración y el elegido queda en azul.
- [ ] "Personalizado" (min / s) cambia la duración; por debajo de 5 s se corrige a 5 s.
- [ ] "Alerta en los últimos (s)" y "Beeps en los últimos (s)" cambian el comportamiento.
- [ ] "Tono al empezar cada ciclo" se puede desactivar.
- [ ] "Restaurar valores del Pez" vuelve a 2:00 · 25 s · 5 s.
- [ ] "Silenciar" quita todo sonido y desactiva el volumen; "Volumen" + "Probar" funcionan.

## 5. Overlay (lo más importante)

Probar con Tibia abierto, primero en **modo ventana** y luego en **pantalla completa**.

- [ ] El overlay (solo el contador) aparece **solo cuando Tibia está al frente**.
- [ ] Al pasar a otra ventana (Alt+Tab, el hub, el navegador) el overlay **desaparece**; al volver a Tibia, reaparece.
- [ ] Se ve **encima de Tibia** en modo ventana.
- [ ] Se ve encima de Tibia en pantalla completa. Si no se ve, anotar qué modo de pantalla usa Tibia.
- [ ] Los **clics atraviesan** el overlay y llegan a Tibia (clic sobre el contador mueve/ataca en el juego).
- [ ] El overlay **no roba el foco**: se puede seguir escribiendo/jugando en Tibia sin interrupciones.
- [ ] En la alerta el overlay se pone rojo y parpadea.
- [ ] "Desbloquear para moverlo": aparece con borde ámbar discontinuo, se puede arrastrar y, al volver a bloquear, los clics vuelven a atravesarlo.
- [ ] La posición se mantiene tras cerrar y abrir la app.
- [ ] "Volver a la esquina superior derecha" lo recoloca.
- [ ] "Tamaño" y "Opacidad" se aplican al overlay en vivo.
- [ ] "Mostrar overlay" desactivado lo oculta aunque Tibia esté al frente.
- [ ] La **vista previa** de la pestaña refleja tamaño, opacidad y colores; "Ver en alerta" la muestra en rojo.

### Colores

- [ ] "Contador", "Contador en alerta" y "Fondo" abren el selector de color y se aplican al overlay y a la vista previa.
- [ ] "Opacidad del fondo" a 0 deja solo el número, con contorno oscuro legible sobre el juego.
- [ ] "Restaurar colores" vuelve al morado por defecto del contador y al fondo oscuro.

## 6. Atajos

- [ ] Con **Tibia al frente**: Ctrl+Alt+P inicia/pausa, Ctrl+Alt+R reinicia, Ctrl+Alt+O muestra/oculta el overlay.
- [ ] En la pestaña Atajos, clic en un atajo → "Pulsa la combinación…" → pulsar p. ej. **F8** lo asigna y funciona con Tibia al frente.
- [ ] **Esc** cancela la grabación sin cambiar nada.
- [ ] Una tecla sin modificador (p. ej. solo "P") se rechaza con un sonido.
- [ ] Asignar a una acción la combinación de otra deja a la otra "Sin atajo".
- [ ] El botón ✕ quita el atajo.
- [ ] Si otro programa ya usa la combinación, aparece un aviso en la bandeja.
- [ ] "Restaurar atajos" vuelve a Ctrl+Alt+P / R / O.
- [ ] Usar un atajo en el juego no escribe nada raro en el chat de Tibia.

## 7. Bandeja del sistema y cierre

- [ ] Cerrar el hub con la X lo oculta; el timer y el overlay siguen funcionando.
- [ ] Doble clic en el ícono de la bandeja vuelve a abrir el hub.
- [ ] Menú de la bandeja: "Abrir Alfa Hub", "Iniciar / pausar", "Reiniciar" y "Salir" funcionan.
- [ ] "Salir" cierra la app por completo (desaparece el ícono y el overlay).

## 8. Ajustes guardados

- [ ] Tras cambiar duración, colores, atajos, tamaño y posición, cerrar con "Salir" y abrir de nuevo: todo se conserva.
- [ ] Existe `%APPDATA%\AlfaHub\ajustes.ini` con esos valores.

## 9. BattlEye

- [ ] Jugar una sesión normal (30+ min) con Alfa Hub abierto y el overlay visible: sin avisos, expulsiones ni errores de BattlEye.

## Qué devolver

Un resumen con ✅ / ❌ por sección, capturas de lo que se vea mal, la versión de Windows, el escalado de
pantalla (100/125/150 %) y el modo de pantalla de Tibia (ventana / pantalla completa).
