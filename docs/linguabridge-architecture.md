# LinguaBridge: estructura del código

Un mapa para orientarse. Cada carpeta tiene una sola responsabilidad. Las clases grandes (`Plugin`, `ConfigWindow`,
`MainWindow`) son `partial`: un archivo por cada zona, todos en el mismo namespace.

```
src/LinguaBridge/
├─ Plugin/                  Punto de entrada (IDalamudPlugin), repartido por responsabilidad
│  ├─ Plugin.cs             Servicios de Dalamud, arranque y cierre, ventanas, guardar config, historial
│  ├─ Plugin.Commands.cs    /lb y /tr
│  ├─ Plugin.Incoming.cs    Chat entrante: qué se traduce y cómo se imprime (debajo / reemplazando, en orden)
│  ├─ Plugin.Outgoing.cs    Enviar traducido: preparar (también la vista previa), dividir y enviar
│  ├─ Plugin.TranslateOnly.cs  Portapapeles (/lb clip) y el modo "Solo traducir"
│  ├─ Plugin.Integrations.cs   Aetherphone (Linkpearl) y el menú del buscador de grupo
│  ├─ Plugin.ServerBar.cs   Entrada "LB" de la barra superior
│  ├─ Plugin.Notices.cs     Avisos al usuario (reservas, pausas de Google, errores) sin inundar el chat
│  └─ Plugin.Helpers.cs     Nombres, canales, prefijos de respuesta, sujeto de los emotes
├─ Config/                  Configuración guardada (¡no cambiar nombres ni namespace de estos tipos!)
│  ├─ Configuration.cs      Ajustes + migraciones entre versiones
│  └─ ConfigurationTypes.cs Enums, proveedores de IA, personajes, glosario
├─ History/                 Historial de la ventana /lb (HistoryEntry, TranslationHistory)
├─ Chat/                    Hablar con el juego: leer mensajes, enviar líneas, portapapeles, /tr, contexto
├─ Translation/             Traducción, sin nada de interfaz (se puede probar fuera del juego)
│  ├─ TranslationService.cs Decide la dirección, el filtro de idiomas, la caché y la cadena de motores de reserva
│  ├─ LanguageFilter.cs     "Todos excepto los que entiendo" / "solo estos"
│  ├─ Languages.cs, PairStyle.cs, LruCache.cs
│  ├─ Engines/              Google (con límite propio y pausa automática), DeepL, IA (OpenAI-compatible)
│  └─ Text/                 Detector de idioma, jerga, translit ruso, texto protegido, marcas de RP, nombres
├─ Windows/                 Interfaz ImGui
│  ├─ Components/           Ui (colores, botones, estados...) y ColorPicker
│  ├─ Main/                 Ventana /lb: barra, historial, redactar
│  ├─ Settings/             Ajustes: un archivo por pestaña
│  └─ Setup/                Asistente inicial y aviso de idioma de la interfaz
├─ Tutorial/                Tour guiado y consejos
└─ Localization/            Loc + ja/de/fr.json (la clave es el texto en inglés)
```

## Flujo de un mensaje entrante

1. `Plugin.Incoming` recibe el mensaje del juego y lo filtra: canal, combate, propio, jugadores, longitud, spam.
2. `MessageReader` lo convierte en texto y protege enlaces y frases del autotraductor.
3. `TranslationService.TranslateAsync` protege nombres y marcadores del juego, mira la caché y detecta el idioma sin
   conexión (`LanguageDetector`). Aplica el `LanguageFilter` antes de gastar una petición y solo entonces llama al motor.
4. El motor (`Engines/`) traduce. Si falla o está en pausa, prueba el siguiente de la cadena (IA → DeepL → Google).
5. `Plugin.Incoming` imprime la traducción y la guarda en `TranslationHistory`.

## Reglas

- Todo texto visible pasa por `L()` / `LF()`. `scripts/check_localization.py` lo comprueba en CI, también los textos
  sueltos que se olviden de `L()`. Una línea que solo muestra códigos o números se marca con `// loc-ok`.
- `Translation/` no usa ImGui ni servicios de Dalamud. Así se puede probar con un motor falso
  (`TranslationService(config, handler)`).
- Los tipos de `Config/` se guardan en el archivo de configuración: renombrarlos o moverlos de namespace rompe la
  configuración de los usuarios.
