# DalamudPlugins

Repositorio personal de plugins de Dalamud para FFXIV.

## Instalar el repositorio en el juego

1. En el juego escribe `/xlsettings` → pestaña **Experimental**.
2. En **Custom Plugin Repositories** pega esta URL, pulsa **+** y luego **Save and Close**:

   ```
   https://raw.githubusercontent.com/AdrianJimenezSantiago/DalamudPlugins/main/repo.json
   ```
3. Abre `/xlplugins` y busca el plugin.

## Plugins

### LinguaBridge — traducción de chat en tiempo real (EN ⇄ FR por defecto)

- **Entrante**: los mensajes de los canales elegidos (Say, Party, Tells, FC, LS, CWLS…) se traducen solos y aparecen debajo del original con una etiqueta `[EN>FR]` / `[FR>EN]`. Solo tú ves la traducción.
- **Saliente**: `/tr <mensaje>` traduce tu texto al otro idioma y lo envía.
  - `/tr Hello!` → canal actual
  - `/tr /p Ready?` → party · `/tr /fc ...` · `/tr /t Nombre Apellido@Mundo ...` · `/tr /t <t> ...`
  - `/tr :en /p Merci` → fuerza el idioma de destino
- `/linguabridge` abre el historial con un cuadro para escribir y previsualizar; `/linguabridge config` abre los ajustes; `/linguabridge on|off` activa o desactiva la traducción entrante.
- **Motores**: DeepL (la mejor calidad para EN/FR, clave gratuita de 500.000 caracteres/mes en deepl.com → API Free) o Google Translate (sin clave, el motor por defecto).

## Desarrollo

- Código en `src/<Plugin>/` (SDK `Dalamud.NET.Sdk`, .NET 10, API 15).
- Cada push a `main` que toque `src/` lanza el GitHub Action `Build plugins`: compila, copia `latest.zip` a `plugins/<Plugin>/` y regenera `repo.json`.
- Para publicar una versión nueva, sube `<Version>` en el `.csproj`.
- Compilación local: `DALAMUD_HOME=<ruta a Dalamud> dotnet build -c Release` y luego `python3 scripts/update_repo.py`.
