# DalamudPlugins

Repositorio personal de plugins de Dalamud para FFXIV.

## Instalar el repositorio en el juego

1. En el juego escribe `/xlsettings` → pestaña **Experimental**.
2. En **Custom Plugin Repositories** pega esta URL, pulsa **+** y luego **Save and Close**:

   ```
   https://raw.githubusercontent.com/Ajimsan2096/DalamudPlugins/main/repo.json
   ```
3. Abre `/xlplugins` y busca el plugin.

## Plugins

### LinguaBridge — traducción de chat en tiempo real (EN ⇄ FR por defecto)

- **Asistente inicial** (se abre solo la primera vez, o con `/lb wizard`): idiomas, motor con prueba en vivo y canales.
- **Entrante**: los mensajes de los canales elegidos (Say, Party, Tells, FC, LS, CWLS…) se traducen solos y aparecen en la misma pestaña de chat con una etiqueta `[EN>FR]` / `[FR>EN]`. Solo tú ves la traducción.
  - Los enlaces de objetos, las frases del auto-translate y las palabras protegidas (nombres, FC…) no se traducen.
  - Se ignoran "gg", "o/", enlaces sueltos, sonidos `<se.X>` y el spam de macros.
  - Desarrolla abreviaturas de chat (jsp, tkt, idk, tbh…) antes de traducir.
- **Saliente**: `/tr <mensaje>` traduce tu texto al otro idioma y lo envía.
  - `/tr Hello!` → canal actual · `/tr /p Ready?` → party · `/tr /fc ...` · `/tr /t Nombre Apellido@Mundo ...` · `/tr /t <t> ...`
  - `/tr :en /p Merci` → fuerza el idioma de destino · `/tr` solo abre la ventana.
- **Ventana** (`/lb`): historial con botón **Reply** (responde traducido en el mismo canal o tell), cuadro para escribir con **Preview** + retrotraducción ("Means: …") para comprobar el sentido antes de enviar.
- **Party Finder**: clic derecho en un anuncio → *Translate description*.
- **Barra de servidor** (arriba a la derecha): `LB EN/FR` — clic izquierdo activa/desactiva, clic derecho abre la ventana.
- **Motores**: DeepL (la mejor calidad para EN/FR, clave gratuita de 500.000 caracteres/mes en deepl.com → API Free) admite **varias claves**: si una agota su cuota mensual (o no es válida) se usa la siguiente para el mismo mensaje, y solo cuando fallan todas pasa automáticamente a Google; o Google directamente (sin clave).
- **Modo rol (RP/ERP)** — `/lb rp`, casilla **RP** en la ventana o pestaña *Roleplay*:
  - Contexto para DeepL de prosa en personaje (tono, voz, escenas maduras sin suavizar).
  - Conserva `*acciones*`, `"diálogo"` / `« diálogo »`, `((OOC))` y `[OOC ...]`.
  - Traduce emotes (`/em`) usando el nombre del personaje como sujeto; `/tr /em sonríe` funciona.
  - Los posts de varias líneas no se descartan como spam de macro; los textos largos se envían en varias líneas `(1/2)`.
  - Notas de escena/personajes para DeepL y formalidad propia para RP.
- **Contexto de conversación**: las últimas líneas del mismo chat se mandan a DeepL como contexto (no se factura) → mejores pronombres, género, tu/vous.
- **Glosario propio** (pestaña *Glossary*): tus traducciones de términos por dirección (EN>FR, FR>EN…), subidas automáticamente como glosarios de DeepL en cada clave.
- **Perfiles de personaje** (pestaña *Roleplay*): género y notas de cada personaje; se mandan a DeepL solo cuando ese personaje está en la conversación → concordancia correcta (il/elle, sûr/sûre) y tono adecuado.
- **Nombres protegidos automáticamente**: el remitente y tu personaje nunca se traducen.
- **Motor de IA** (pestaña *AI*): cualquier API compatible con OpenAI (Mistral, Groq, Cerebras, OpenRouter, DeepSeek, Gemini, o un modelo local con Ollama / LM Studio), en cadena con respaldo automático: si uno llega al límite, falla o se niega, se usa el siguiente; si fallan todos, DeepL / Google. Por defecto solo en modo rol. Usa el contexto de la conversación, los perfiles de personaje y el glosario.
- **Reemplazar el original** (pestaña *Chat* → *How to show translations*): muestra solo la traducción, en el mismo orden, para que los posts largos de rol no salgan duplicados (siempre, o solo en modo rol).
- **Español ↔ alemán ↔ inglés afinado**: abreviaturas alemanas (hdl, ka, vllt, iwie, haste, biste…) y palabras sin diéresis (fuer, schoen, moechte…), más abreviaturas españolas (ntp, tkm, bss, grax, xo, weno…) y palabras sin tilde; reglas de estilo para alemán (du, casos y género correctos, partículas modales), notas de lectura del alemán (doch/halt/mal, «Na?», «oder?») y del español de España (tío/tía, vale, venga, qué fuerte); ejemplos ES>DE, DE>EN, EN>DE y DE>ES para la IA; nombres oficiales de oficios FFXIV EN↔DE en el glosario recomendado.
- Comandos: `/lb` · `/lb config` · `/lb wizard` · `/lb on|off|toggle` · `/lb rp` · `/tr`.

## Desarrollo

- Código en `src/<Plugin>/` (SDK `Dalamud.NET.Sdk`, .NET 10, API 15).
- Cada push a `main` que toque `src/` lanza el GitHub Action `Build plugins`: compila, copia `latest.zip` a `plugins/<Plugin>/` y regenera `repo.json`.
- Para publicar una versión nueva, sube `<Version>` en el `.csproj`.
- Compilación local: `DALAMUD_HOME=<ruta a Dalamud> dotnet build -c Release` y luego `python3 scripts/update_repo.py`.
