# LinguaBridge: auditoría UI/UX y rediseño (v1.13.0)

Alcance: ventana principal (`/lb`), ajustes (`/lb config`), asistente inicial (`/lb wizard`) y burbujas del tour/tips.
Criterios: claridad, jerarquía visual, consistencia, accesibilidad (contraste, escalado, no depender solo del color,
tooltips en iconos, prevención de errores) y estética moderna/minimalista dentro de los límites de ImGui.

## 1. Hallazgos

| # | Gravedad | Área | Problema | Solución aplicada |
|---|----------|------|----------|-------------------|
| 1 | Alta | Todas | Anchos fijos en píxeles (`170`, `220`, `-150`...) que no respetan la escala global de fuente de Dalamud: con la escala al 150 % los combos cortan el texto y los botones se solapan. | Todos los tamaños pasan por `Ui.S()` (`ImGuiHelpers.GlobalScale`). |
| 2 | Alta | Todas | El texto secundario usa `TextDisabled` (~4:1 de contraste) para información que hay que leer (estado, avisos, ayudas). | Nuevo token `Ui.Muted` (≈8:1) para el texto secundario. `TextDisabled` solo queda para controles deshabilitados. |
| 3 | Alta | Principal | El estado (traduciendo / en pausa) solo se ve en una casilla sin contexto; los mensajes recibidos y enviados se distinguen **solo por el color** (amarillo/azul). | Interruptor con texto "Translating" / "Paused". Cada mensaje lleva flecha ↓/↑ y borde de color, más un tooltip ("Received" / "Sent by you"). |
| 4 | Alta | Principal | "Clear" borra todo el historial con un clic, sin confirmar. | Icono de escoba con ventana de confirmación ("Clear all N translations?"). |
| 5 | Media | Principal | Historial como texto plano: cabecera, botones "Copy"/"Reply" y original sin separación visual; cuesta escanearlo. | Tarjetas: remitente, insignias de canal e idioma (`EN > FR`), hora y acciones alineadas a la derecha; traducción en texto normal; original atenuado. |
| 6 | Media | Principal | La altura de la zona de redacción se calculaba con números mágicos y se descuadraba en modo RP o con vista previa. | Se mide cada frame; el historial ocupa exactamente el resto. |
| 7 | Media | Principal | Los errores, el éxito y el progreso se mostraban igual (texto gris). No había indicador de "traduciendo". | Línea de estado con icono y color por tipo (info, éxito, aviso, error) y un spinner animado mientras traduce. |
| 8 | Media | Principal | Los botones de acción no tenían jerarquía: Preview y Send se veían iguales. | Un único botón primario por zona (Send / Translate & copy), con icono; los demás, secundarios. |
| 9 | Media | Principal | El contador de bytes en RP no avisaba al pasar el límite de 500. | Se vuelve ámbar con un mensaje claro: "longer than one chat line, it will be split". |
| 10 | Media | Ajustes | Pestañas largas sin estructura (casillas una tras otra, separadores sin título). La barra de pestañas desaparece al desplazarse. | Encabezados de sección con icono y regla; cada pestaña tiene su propia zona con scroll y la barra queda fija. Los canales se muestran en dos columnas. |
| 11 | Media | Ajustes | Botones "X" y "Up" sin explicar (no se entiende qué hacen ni se pueden leer con lector de tooltips). | Iconos de papelera y flecha, todos con tooltip ("Remove this key", "Use this key earlier"...). |
| 12 | Media | Ajustes | Los avisos de privacidad se perdían como un texto más. | Recuadro "callout" con escudo, en ámbar. |
| 13 | Media | Asistente | "Step 1 of 4" en texto; se podía avanzar con los dos idiomas iguales. | Indicador de pasos con nombre (Languages, Engine, Channels, Done); "Next" se desactiva y explica por qué mientras los idiomas coinciden. |
| 14 | Baja | Asistente | Botones del pie colocados con desplazamientos a ojo (`-180`, `-270`). | "Back" o "Skip setup" a la izquierda, acción primaria alineada a la derecha según su ancho real. |
| 15 | Baja | Asistente/Ayuda | Chuleta de comandos en un bloque de texto con espacios para alinear. | Tablas comando / descripción; en Ayuda, un clic copia el comando. |
| 16 | Baja | Todas | Tooltips que no aparecen sobre botones deshabilitados (el usuario no sabe por qué no puede pulsar). | `Ui.Tooltip` usa `AllowWhenDisabled`. |
| 17 | Baja | Tour | Colores y tamaños de las burbujas definidos aparte, sin escala. | Usan los mismos tokens y la misma escala. Los textos del tour se actualizaron a los nuevos controles. |

## 2. Sistema de diseño (`Windows/Ui.cs`)

- **Colores**: `Accent` (azul, acciones y mensajes enviados), `Incoming` (ámbar), `Success`, `Warning`, `Danger`, `Learning`, `Muted`.
  Todos con contraste ≥ 4.5:1 sobre el fondo oscuro por defecto de Dalamud.
- **Escala**: `Ui.S(px)`, `Ui.FieldWidth`, `Ui.Rounding`.
- **Componentes**: `Section`, `Hint`, `MutedText`, `Icon`, `Status` (+ spinner), `Tooltip`, `IconButton` / `GhostIconButton`
  (tooltip obligatorio), `Button` (icono + texto, opción primaria), `Segmented`, `Badge`, `Callout`, `AlignRight`.
- **Reglas**: un solo botón primario por zona; el color nunca es la única señal (siempre va con un icono o una palabra); todo icono suelto lleva tooltip;
  las acciones destructivas piden confirmación o son reversibles.

## 3. Pendiente / recomendaciones

- No se pudo comprobar dentro del juego (este entorno no ejecuta FFXIV). Hay que revisar en el cliente:
  alineación vertical de la cabecera de las tarjetas, escala al 100 / 150 / 200 % y temas claros de Dalamud.
- Localizar la interfaz (ahora solo en inglés) y añadir más idiomas de UI.
- Navegación con teclado: un atajo para enfocar el cuadro de texto y otro para la última respuesta.
- Opción para reducir el movimiento (el spinner y el pulso del tour), para quien tenga sensibilidad al movimiento.
