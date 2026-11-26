# LinguaBridge 1.15.0: auditoría completa

**Alcance:** todo `src/LinguaBridge`: núcleo de traducción, motores (Google, DeepL, IA), detector de idioma, jerga,
transliteración rusa, chat entrante y saliente, las 5 ventanas (principal, ajustes, asistente, aviso de idioma,
tour/consejos) y las traducciones de la interfaz (ja/de/fr).

**Método:** lectura completa del código, más un banco de pruebas **offline** que compila el detector de idioma, la jerga,
`ProtectedText`, `MixedText` y el analizador de `/tr` y los ejecuta con frases de ejemplo. **No se hizo ninguna llamada
a Google, DeepL ni a la IA.** No se pudo probar dentro del juego.

> **Estado (1.16.0):** corregido todo lo de las secciones 1 a 4, y hecha la propuesta de la sección 5 (idiomas a no
> traducir), más la pausa automática de Google con reanudación sola. Sin hacer, a propósito: T11 (variantes ES/PT, no
> se quieren) y las ideas de la sección 6. Sin poder verificar: "Hurler" / "Crier" en el cliente francés.

Gravedad: **Alta** = traducción errónea, pérdida de mensajes o bloqueo del motor · **Media** = funciona mal en casos
habituales · **Baja** = detalle o pulido.

---

## 1. Errores de traducción (calidad)

| # | Grav. | Dónde | Problema (confirmado en las pruebas offline) | Arreglo propuesto |
|---|---|---|---|---|
| T1 | **Alta** | `Translation/Slang.cs:100` (ES), `:134` (PT) | `pq` / `xq` se expanden siempre a **"porque"**, pero en el chat casi siempre son **"por qué"** en preguntas. `pq no vienes?` pasa a `porque no vienes?`: la pregunta se convierte en una causa y la traducción sale mal ("because you don't come?"). Lo mismo en portugués (`pq nao veio?`). | Si el mensaje acaba en `?` o la abreviatura va al principio, expandir a "por qué" / "por que". Si no, dejar la abreviatura (los motores ya la entienden). |
| T2 | **Alta** | `Slang.cs:52` (EN) | `prob` → "probably", pero `no prob` es "no problem": sale **"no probably"**. | Quitar `prob` o tratar `no prob` como frase. |
| T3 | Media | `Slang.cs:112` (ES) | `sabia` → "sabía", pero **"sabia" es una palabra** ("eres muy sabia" → "eres muy sabía"). Va contra la regla del propio archivo ("unambiguous ones only"). | Quitar `sabia`. |
| T4 | Media | `Slang.cs:127` (ES) | `sonrio` → "sonrío" (yo), pero sin tilde también es **"sonrió"** (él/ella), muy habitual en emotes de RP en tercera persona: cambia la persona del verbo. | Quitarlo, o elegir según el contexto (dentro de `/em` o `*...*` en tercera persona → "sonrió"). |
| T5 | Media | `Slang.cs:32` (FR) | `mm` → "même", pero en el chat `mm` suele ser "hmm": `mm d'accord` → "même d'accord". | Quitar `mm`. |
| T6 | Baja | `Slang.cs:27` (FR), `:52` (EN) | Otras expansiones dudosas: `ct` (c'était / c'est), `def` (definitely / defensa). | Revisarlas una a una con la regla "una sola lectura posible". |
| T7 | **Alta** | `Translation/PairStyle.cs:310-314` (glosario recomendado) | Los glosarios de DeepL sustituyen **cada aparición** del término, también fuera de un apelativo: `amor` → "mon amour" ("hacer el amor" → "faire le mon amour"), `reina` → "ma reine" ("la reina de Ul'dah" → "la ma reine"), `queen` → "ma reine", `sweetheart` → "mon cœur" ("my sweetheart" → "mon mon cœur"). | Para apelativos, usar entradas con posesivo (`mi amor` → `mon amour`) y quitar las de una sola palabra (`amor`, `reina`, `queen`, `sweetheart`). |
| T8 | Media | `PairStyle.cs:339-342, 357` | Los oficios genéricos del glosario (`Dancer` → "Danseur" / "Tänzer", `Warrior` → "Guerrier", `Monk` → "Moine", `Reaper` → "Faucheur") fuerzan el masculino y la mayúscula también en prosa: "she's a dancer" → "c'est un Danseur". | Dejar en el glosario solo los nombres compuestos y sin ambigüedad (Dark Knight, White Mage, Gunbreaker...). Los nombres de una palabra, mejor como términos protegidos o pistas para la IA. |
| T9 | Baja | `PairStyle.cs` (glosario FR) | Faltan oficios oficiales en francés: Sage, Viper (Rôdeur vipère), Pictomancer (Pictomancien), Samurai (Samouraï), Blue Mage (Mage bleu), Limit Break (Transcendance). | Añadirlos. |
| T10 | Media | `Plugin.cs:403-412` | En los emotes, el nombre se quita con `StartsWith`. En japonés el sujeto va seguido de partícula ("Nameは微笑む"), así que queda **"は微笑む"**. Si el motor cambia el orden o la forma del nombre, no se quita y el nombre aparece dos veces. | Quitar también las partículas japonesas (は/が/も) tras el nombre y, si no se encuentra el nombre, buscar el marcador protegido. |
| T11 | Media | `PairStyle.cs:119` + `Languages.cs:119, 122` | El español de destino es siempre **español de España** ("vale", "tío"...) y el portugués siempre **PT-BR**. Para jugadores de Latinoamérica o de Portugal suena raro. DeepL ya acepta `ES-419` y `PT-PT`. | Opción "Variante": ES (España / Latinoamérica) y PT (Brasil / Portugal), como ya existe EN-US/EN-GB. |
| T12 | Media | `PairStyle.cs` | No hay reglas de estilo ni pistas de DeepL para **IT, PT ni JA**. El japonés es el caso más importante: sin pista, la IA puede elegir keigo o un registro muy literal. | Añadir `TargetRules` / `DeepLHint` para JA (registro casual entre jugadores, sin keigo salvo en escenas formales), IT y PT. |
| T13 | Baja | `ConfigWindow.cs:38-41, 257` | Formalidad: las etiquetas "Informal (tu)" / "Formal (vous)" solo hablan del francés (en japonés aparece "くだけた（tu）"). Además el texto dice "FR/DE/ES/IT/PT/JA/RU" pero DeepL también admite formalidad en **NL**. | Etiquetas neutras ("Informal (tú/du/tu)") y añadir NL. |

### Detector de idioma (pruebas offline)

Funciona bien con frases normales: el portugués no se confunde con el español, el neerlandés no se confunde con el alemán
y distingue las frases en japonés con kana. Puntos débiles:

| # | Grav. | Problema | Ejemplos (par EN/ES) |
|---|---|---|---|
| D1 | Media | Las frases cortas en español no se reconocen y acaban en el motor (una petición a Google por mensaje). | `me voy a dormir`, `vamos?`, `hola gente`, `q tal` → `null` |
| D2 | Baja | Faltan palabras españolas muy frecuentes en la lista: `gente` (solo está en PT), `hasta`, `desde`, `tiene`, `fue`. Tampoco puntúa la `é` del español ("qué", "café"), que cuenta para el francés. | `Hola gente` empata ES/PT |
| D3 | Baja | `LanguageDetector.cs:233`: el rango `FF00-FFEF` incluye el **katakana de ancho medio** y lo descarta como "CJK no japonés". | `ｵｹ` → `null` |
| D4 | Baja | `LanguageDetector.cs:138-142`: dos regex idénticas (`LongRunRegex` y `LongRun`). | Limpieza |

---

## 2. Peticiones a Google (riesgo de bloqueo de IP)

Es lo que más afecta al bloqueo de IP. Cada punto suma peticiones que no hacen falta:

| # | Grav. | Dónde | Problema | Arreglo |
|---|---|---|---|---|
| G1 | **Alta** | `TranslationService.cs:150-162` | En el modo por defecto (solo hacia el idioma que mejor lees), con "traducir otros idiomas" **desactivado**, el filtro se aplica **después** de llamar al motor (línea 160): se gasta la petición y luego se descarta. | Descartar antes de llamar cuando la detección local ya indica un tercer idioma. Si no hay detección, que no se llame dos veces. |
| G2 | **Alta** | `TranslationService.cs:166-191` | En modo bidireccional, un mensaje dudoso cuesta **2 peticiones**: primero se traduce a B para que el motor detecte el idioma y luego se repite hacia A o hacia el idioma preferido. | Primera llamada directa al destino más probable (el idioma preferido) y reutilizar el resultado si coincide. |
| G3 | **Alta** | `GoogleTranslator.cs:23-31` + `ProtectedText.cs:206-235` | Si el texto lleva nombres o enlaces protegidos y Google devuelve el marcador `{0}` alterado (en japonés suele salir `｛0｝`, de ancho completo) o el jugador escribió `{0}`, se hace una **segunda petición** con el texto plano. Además, con el nombre protegido automáticamente, casi todos los mensajes llevan marcadores. | Aceptar `｛0｝` en la regex, usar marcadores menos frágiles (p. ej. `[[1]]`, un token raro) y escapar las llaves que escriba el jugador. |
| G4 | **Alta** | `GoogleTranslator.cs:39-43` | Un **429** no detiene nada: cada mensaje nuevo vuelve a intentarlo, y eso es justo lo que alarga un bloqueo. | "Cortacircuitos": tras un 429, pausar Google N minutos (con espera creciente), avisar una vez y no volver a llamar durante la pausa. |
| G5 | Media | `TranslationService.cs:122-131` | La caché solo se rellena al terminar: si llegan a la vez dos mensajes idénticos (macros, varios jugadores escribiendo "¡felicidades!"), se envían dos peticiones. | Compartir la tarea en curso (`ConcurrentDictionary<string, Task<...>>`). |
| G6 | Media | `TranslationService.cs:221-235` | Los mensajes mezclados (RP con `*acción*` en otro idioma) hacen **una petición por trozo**. | Agrupar los trozos en una sola llamada (Google acepta varios `q=`). |
| G7 | Media | `GoogleTranslator.cs:36-39` | Se usa **GET** con el texto en la URL: con 3000 caracteres en japonés (`/lb clip`) la URL supera ~25 KB y el servidor puede rechazarla (414/400). | Usar POST con el cuerpo `application/x-www-form-urlencoded`. |
| G8 | Baja | `MainWindow.cs:526-530` | Cada "Vista previa" hace dos peticiones (traducción y traducción de vuelta). | Hacer la traducción de vuelta solo si se pide (botón "¿Qué significa?"). |
| G9 | Baja | — | No hay límite propio de peticiones por minuto. | Limitador (p. ej. 20-30 por minuto) con cola. Lo que no quepa se muestra sin traducir y con aviso. |

---

## 3. Fallos de funcionamiento

| # | Grav. | Dónde | Problema | Arreglo |
|---|---|---|---|---|
| B1 | **Alta** | `TranslationService.cs:59` + `LlmTranslator.cs:310-311` | El `HttpClient` compartido tiene **Timeout = 10 s**, así que el `CancelAfter(20 s)` de la IA nunca llega a actuar: los modelos lentos (OpenRouter `:free`, Ollama local, posts largos de RP) se cortan a los 10 s y el proveedor queda en pausa 30 s. | `Timeout = Infinite` en el cliente y un plazo por motor (Google/DeepL 10 s, IA 30 s). |
| B2 | **Alta** | `LlmTranslator.cs:63-66, 271` | El detector de "rechazos" de la IA marca como rechazo **traducciones correctas** que empiezan por "No puedo...", "Lo siento...", "I'm sorry..." cuando el original no empieza igual (FR "Je n'arrive pas à y croire" → ES "No puedo creerlo"; "Désolée pour le retard" → "Lo siento por el retraso"). El proveedor queda **10 minutos en pausa**. | Exigir que, además, la respuesta no tenga la etiqueta `[[xx]]` o que sea larga y sin relación (p. ej. que mencione "AI", "content", "policy"). Lo más fiable es pedir el formato `[[xx]]` y considerar rechazo solo cuando falta. |
| B3 | Media | `LlmTranslator.cs:325-328` | Cualquier error 400 cuyo texto contenga "content" se clasifica como **filtro de contenido** (10 min de pausa). Muchos errores normales lo contienen (`messages[1].content`...). | Buscar `content_filter`, `content_policy` o `safety`, no "content" a secas. |
| B4 | **Alta** | `Plugin.cs:478-485 vs 507` | **Modo "Reemplazar": mensajes desordenados.** Los mensajes que se filtran antes (cortos tipo "lol", tus propios mensajes, otros canales) se muestran al instante, mientras que los anteriores aún esperan su traducción (hasta 12 s): un "jajaja" aparece **antes** del mensaje al que responde. | Meter en la misma cadena de salida todos los mensajes de los canales vigilados mientras haya traducciones pendientes, o al menos los del mismo canal. |
| B5 | Media | `Plugin.cs:705-706` | En "Reemplazar", la línea de aprendizaje se espera **dentro** de la cadena (hasta 6 s por mensaje): con el idioma de aprendizaje activo y un chat rápido, todo se va retrasando. | Imprimir la línea de aprendizaje fuera de la cadena, o reducir la espera a ~1-2 s. |
| B6 | Media | `MainWindow.cs:509-541` vs `Plugin.cs:347-361` | **La vista previa no coincide con lo enviado.** La vista previa traduce sin contexto ni nombres protegidos y "Enviar" vuelve a traducir con ellos: puede salir otro texto (con la IA, casi siempre) y se gasta otra petición. | Si hay vista previa del mismo texto, enviar **exactamente** la vista previa (botón "Enviar esta"). |
| B7 | Media | `Plugin.cs:356` + `ChatSender` | Los marcadores del juego (`<t>`, `<flag>`, `<pos>`, `<me>`, `<se.1>`) no se protegen en `/tr` ni en la ventana: se envían al motor, que puede traducirlos o meter espacios ("< bandera >") y romper el marcador. | Protegerlos con `ProtectTerms` / una regex `<[a-z0-9.]+>` antes de traducir. |
| B8 | Media | `Chat/OutgoingCommand.cs:426-434` | `/tr :D hola` o `/tr :3 nya` da **error** ("Unknown language 'D'"): cualquier emoticono con dos puntos al principio se toma como idioma. | Solo tratarlo como idioma si coincide con un código; si no, es texto. |
| B9 | Media | `Windows/SetupWizard.cs:295` | La **prueba del asistente da "Funciona"** sin haber traducido nada cuando ninguno de los dos idiomas es inglés (p. ej. ES/DE): el texto de prueba en inglés es un tercer idioma, el servicio devuelve `null` y se muestra "Works (the text was already in that language)". | Usar una frase de prueba en el idioma A y `translateOthers: true`. |
| B10 | Media | `MainWindow.cs:189-193` | Saltar arriba al llegar un mensaje depende de que **cambie el número de entradas**: al llegar al tope (200) deja de cambiar y ya no salta. Y, cuando sí salta, te mueve aunque estés leyendo más abajo. | Usar un contador de mensajes que siempre aumente y saltar solo si ya estabas arriba del todo. |
| B11 | Media | `ConfigWindow.cs:699` | El botón "Añadir {último remitente}" excluye "Listing" y "You", pero **no "Clipboard"**: tras usar `/lb clip` aparece "Añadir Clipboard". | Guardar si el remitente es un jugador (campo en `HistoryEntry`) en vez de comparar cadenas. |
| B12 | Baja | `Plugin.cs:487-488, 575-576` | La lista blanca y los "jugadores de aprendizaje" comparan con `Contains`: "Al" deja pasar a "Alice" y "Sal". | Coincidencia de palabra o nombre completo (o `Nombre@Mundo`). |
| B13 | Baja | `Plugin.cs:463` | `senderName.EndsWith(me)` puede tomar como tuyo el mensaje de otro jugador cuyo nombre termine igual. | Comparar el nombre completo tras quitar los glifos. |
| B14 | Baja | `ConfigWindow.cs:449-450` | Si la tabla de canales no se crea, `return` corta **toda la pestaña Chat** y no guarda. | `if (table) { ... }` como en el resto. |
| B15 | Baja | `ConfigWindow.cs:369, 400, 493, 645, 729` | Los campos de texto **guardan la configuración en disco y vacían la caché con cada tecla**. | Guardar al perder el foco (`IsItemDeactivatedAfterEdit`). |
| B16 | Baja | `ConfigWindow.cs:325` | En Ajustes se puede guardar A = B (solo se muestra un aviso); el asistente sí lo impide. | Bloquear el cambio o no guardar mientras sean iguales. |
| B17 | Baja | `SetupWizard.cs` | Si cierras el asistente con la X, vuelve a salir en **cada** inicio del juego. | Al cerrar con la X, preguntar u ofrecerlo como "omitir". |
| B18 | Baja | `TranslationService.cs:365-371` | `Dispose` libera el semáforo mientras puede haber tareas esperando: `ObjectDisposedException` al descargar el plugin. | Cancelar, esperar brevemente y luego liberar. |
| B19 | Baja | `LlmTranslator.cs:114` / `DeepLTranslator.cs:38` | `LastProvider` y `current` se comparten entre hilos: con traducciones simultáneas, el historial puede mostrar el proveedor equivocado. | Devolver el proveedor en el resultado. |
| B20 | Baja | `Plugin.cs:480` | El límite de longitud en RP está fijo en 600 e ignora el control deslizante. Los mensajes largos se descartan **sin aviso**. | Hacerlo configurable y mostrar "(demasiado largo, pulsa para traducir)" en el historial. |
| B21 | Baja | `Plugin.cs:630-637` | Con "en el mismo canal", la traducción de un **tell** se imprime como un tell entrante del jugador: otros plugins (avisos sonoros, puentes a Discord, Chat 2) lo ven como un segundo tell. | Avisarlo en la ayuda y ofrecer "tells → Echo". |

---

## 4. Interfaz, ventana por ventana

### Ventana principal (`/lb`)
- Textos **sin traducir**: "Translating...", "Sent." (`MainWindow.cs:512, 550, 581, 590`) y los botones "Copy" / "Dismiss" de la vista previa (`:478, :484`).
- El selector de destino solo ofrece A o B (`:388`): para escribir a un alemán teniendo el par EN/FR hay que usar `/tr :de`. Proponer "Otro idioma..." con todos.
- Faltan canales de envío (`:19-25`): LS 4-8, CWLS 4-8, Novatos, Equipo PvP y **tell a un jugador concreto** (hoy solo "Responder" o `/tr /t`).
- La insignia "EN <> FR · Google" (`:115`) no indica cuándo traduce la **IA** (modo RP).
- `L(e.Sender)` (`:237, 267`) también pasa los **nombres de jugador** por la tabla de traducción: sería mejor traducir solo los remitentes especiales.
- `plugin.History` crea una lista invertida nueva varias veces por fotograma (`:125, 146, 173`). Mejor una vez por fotograma.
- Faltan búsqueda y filtros en el historial (por canal o jugador) y exportarlo.

### Ajustes (`/lb config`)
- **Motor:** el estado de las claves ("not used yet", "OK", "(skipped for now)", "monthly quota used up") y los errores de DeepL, Google e IA salen siempre en inglés. Lo mismo pasa con el estado del glosario.
- **Idiomas:** los nombres de idioma no se traducen. Un usuario de la interfaz japonesa ve "Español", "Nederlands" o **"Russkiy"** (`Languages.cs:126`). Mejor "Русский" o el nombre en el idioma de la interfaz.
- **Idiomas:** solo hay una casilla "traducir también otros idiomas". Ver la propuesta de la **sección 5**.
- **Chat:** los colores se eligen escribiendo un número de UIColor sin ver el color (`:418, :541`). Proponer una paleta con muestra.
- **Chat:** el combo "Canal de salida" muestra los nombres del enum (`Echo`, `Debug`, `SystemMessage`) sin traducir (`:532`). El tamaño del historial (`HistorySize`) no aparece en ningún sitio.
- **IA:** "Testing..." (`:861`) y la insignia "detected" de Aetherphone (`:1105`) sin traducir. El color del resultado se decide comparando textos (`:873`), y eso falla según el idioma.
- **Glosario:** dice "DeepL only", pero la IA también lo usa. Con el motor por defecto (Google) **no se aplica y no se avisa**: falta un aviso cuando el motor es Google. Tampoco se pueden editar las entradas: hay que borrarlas y volver a crearlas.

### Asistente
- Falso "Funciona" en la prueba (B9). "Testing..." sin traducir (`SetupWizard.cs:287`).
- No pregunta qué idiomas no quieres traducir ni si quieres traducir otros idiomas.

### Aviso de idioma de interfaz y tour
- Sin problemas funcionales. Como pedía la auditoría anterior, falta una opción para **reducir el movimiento** (spinner, pulso del tour).

### Traducción de la interfaz (ja/de/fr)
- `check_localization.py` pasa: 452 entradas, 0 faltan. Lo de arriba son textos que **no pasan por `L()`**, por eso el script no los detecta.
- **Por verificar en el cliente francés:** `Yell` → "Hurler" y `Shout` → "Crier". En el juego en francés podrían ser al revés (`/crier` y `/hurler`).
- En japonés, `Say`, `Yell` y `Tell` se quedan en inglés, pero `Shout` → "シャウト": no es coherente.
- Mejora del script: avisar de llamadas `SetStatus("...")`, `PrintError("...")`, `throw new ...Exception("...")` con texto literal fuera de `L()`.

---

## 5. Propuesta: "Idiomas que NO quiero traducir"

Hoy, con los valores por defecto (`TranslateOtherLanguages = true`, solo hacia el idioma preferido), **se traduce todo
lo que no esté ya en tu idioma**. Un hispanohablante que también lee inglés recibe traducido cada mensaje en inglés y
gasta una petición a Google por cada uno.

**Diseño propuesto** (pestaña Idiomas y paso 1 del asistente):

```
Traducir al idioma que mejor leo:
  (•) Todos los idiomas, excepto los que entiendo:   [x] Español (siempre)  [x] English  [ ] Français  [ ] Português ...
  ( ) Solo estos idiomas:                             [x] Français  [x] Deutsch ...
```

- Nueva propiedad `List<string> UnderstoodLanguages` (y `OnlyLanguages` para el segundo modo).
- **Migración:** `TranslateOtherLanguages = false` equivale a "solo A y B"; `true` equivale a "todos excepto el preferido".
- **En `TranslationService`:** si la detección local (con **todos** los idiomas, no solo el par) da un idioma de la
  lista → `null` **sin llamar al motor** (menos peticiones). Si la detección es dudosa y el motor devuelve uno de esos
  idiomas, también `null`. La lista se pasa como un parámetro más, igual que hoy `translateOthers`.
- **Excepciones por jugador:** "traducir siempre a X" o "no traducir nunca a X" (por ejemplo, tu pareja de RP).
- **Detalle de interfaz:** el idioma preferido aparece marcado y bloqueado ("siempre").

---

## 6. Más sugerencias e ideas

**Fiabilidad y coste**
1. Pausa automática de Google ante 429 (G4) y un limitador propio (G9). Mostrar en la barra del servidor "LB ⏸ Google"
   mientras dure la pausa.
2. Un motor gratuito de reserva con una API oficial: **Microsoft Translator** (2 M de caracteres al mes gratis, con
   clave) o un **LibreTranslate** propio, para no depender del endpoint no oficial de Google.
3. Ampliar la caché (512 → unas 2000 entradas) y guardar en disco las frases frecuentes ("ty", "gl", "gracias"...) para
   que sobrevivan a los reinicios.
4. Contador de peticiones por motor en la pestaña Motor (hoy solo se cuentan caracteres).

**Funciones**
5. Menú contextual sobre un **mensaje del chat** o un jugador: "Traducir este mensaje" / "Traducir siempre a este
   jugador" / "No traducir nunca a este jugador".
6. Clic en una traducción del chat para ver el original (hoy solo está en `/lb`).
7. **Idioma por jugador recordado**: si alguien siempre escribe en alemán, responderle en alemán automáticamente con
   "Responder", aunque el par sea EN/FR.
8. Selector de variante: ES España / Latinoamérica, PT Brasil / Portugal (T11).
9. Traducir también los **mensajes de la Free Company / del tablón** y las **etiquetas de búsqueda de grupo** además de
   la descripción.
10. Atajo de teclado para enfocar la caja de texto de `/lb` y para "responder al último".
11. Exportar e importar el glosario y los personajes (CSV) para compartirlos con el grupo de RP.
12. Más idiomas de chat con poco esfuerzo: coreano y chino (el motor ya los detecta; falta mostrarlos, aunque la fuente
    del cliente global no tiene hangul), polaco y turco (muy presentes en los centros de datos europeos).

**Calidad**
13. Pruebas automáticas: el banco de pruebas offline de esta auditoría (detector, jerga, `/tr`, `MixedText`) cabe en un
    proyecto `LinguaBridge.Tests` sin Dalamud y puede ejecutarse en GitHub Actions antes de compilar.
14. Regla para la jerga: cada abreviatura nueva con al menos una frase de prueba que demuestre que no tiene otra lectura.

---

## 7. Evidencia de las pruebas offline (extracto)

```
ES: pq no vienes?        => porque no vienes?        (T1)
EN: no prob              => no probably              (T2)
ES: eres muy sabia       => eres muy sabía           (T3)
FR: mm d'accord          => même d'accord            (T5)
[EN,ES] me voy a dormir  -> null  (se pregunta al motor)   (D1)
[EN,ES] vamos?           -> null                           (D1)
Google: "use {0} then <Rukenu>"  -> marcador duplicado -> 2.ª petición  (G3)
Google: "こんにちは｛0｝"         -> no se reconoce      -> 2.ª petición  (G3)
"heal <t> at <flag>"     -> sin proteger                    (B7)
/tr :D hola              => ERROR: Unknown language 'D'      (B8)
```
