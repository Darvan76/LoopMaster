# LooperMaster

Una pequeña aplicación para Windows que crea un vídeo en bucle usando FFmpeg.

## Cómo utilizarla

1. Abre `LooperMaster.exe`. El programa comprueba si FFmpeg funciona en tu equipo y, si falta, lo descarga automáticamente. Espera a que aparezca **FFmpeg listo**.
2. Arrastra un vídeo a la ventana o pulsa **Elegir vídeo**.
3. Elige cómo quieres crear el bucle:
   - **Repeticiones**: indica cuántas veces quieres que aparezca el vídeo. Por ejemplo, **3** lo reproduce tres veces en total. Este modo recodifica el resultado con los ajustes de compresión seleccionados.
   - **Duración rápida**: indica horas, minutos y segundos. Puedes copiar las pistas originales o activar **Comprimir el clip una vez antes de repetir**. Esta última opción comprime sólo el original y después lo repite por copia, para evitar recodificar todas las horas del bucle.
4. Si quieres, cambia la ubicación o el nombre del archivo de salida.
5. Pulsa **Crear vídeo** y espera a que termine. También puedes cancelar el proceso.

Cuando se recodifica, sólo tienes que elegir **CPU** o **GPU**. El programa detecta automáticamente si puede usar AMD AMF o NVIDIA NVENC para el códec elegido y muestra el motor detectado debajo del selector. Si ambos funcionan, prioriza NVIDIA; si sólo uno admite ese códec, utiliza ese. CPU usa x264 para H.264 y x265 para HEVC. Al abrirse, la aplicación comprueba ambos códecs de cada fabricante mediante una pequeña codificación real, sin bloquear la ventana. GPU queda desactivada si no hay un motor compatible. Si una GPU falla durante la exportación, muestra el error y permite elegir CPU; no cambia a CPU en silencio.

La ventana se puede redimensionar y maximizar. En una ventana estrecha los ajustes se distribuyen en una columna y puedes desplazarte verticalmente. La barra para crear, cancelar o mostrar el archivo permanece visible. El panel de resultado reúne duración, tamaño orientativo y destino; pasa el cursor sobre un nombre largo para ver la ruta completa.

En **Duración rápida**, los controles de compresión se activan al marcar la compresión previa. Con la casilla desmarcada, se conserva el bitrate, el códec, la resolución, los fps y el audio original; CPU/GPU y la compresión quedan desactivados.

## Bitrate y reducción de tamaño

En **Compresión y tamaño** puedes elegir un perfil y modificar sus ajustes. Al modificar un ajuste el perfil pasa a **Personalizado**.

| Perfil | Vídeo | Resolución y fps máximos | Audio |
| --- | --- | --- | --- |
| Alta calidad | H.264, calidad alta | Originales | AAC 192 kbps |
| Equilibrado | H.264, calidad equilibrada | 1920 × 1080, 30 fps | AAC 128 kbps |
| Archivo pequeño | H.265/HEVC, mayor compresión | 1280 × 720, 30 fps | AAC 96 kbps |

- **Control de calidad**: alta, equilibrada o más compresión. El bitrate varía según el contenido. CPU usa CRF, AMD usa QP constante y NVIDIA usa calidad objetivo CQ con bitrate variable. Las escalas y los resultados no son idénticos entre motores.
- **Bitrate manual**: selecciona esta opción en Control de calidad e introduce el bitrate de vídeo en **kbps**. Por ejemplo, 4000 kbps son 4 Mbps. Se utiliza un bitrate objetivo variable, con margen para picos; no garantiza un tamaño exacto ni rellena vídeos simples para alcanzar el objetivo. El audio tiene su propio bitrate.
- **Códec**: H.264 ofrece amplia compatibilidad. HEVC puede ahorrar espacio, pero la codificación CPU puede ser más lenta y el reproductor debe admitirlo.
- **Resolución máxima**: original, 1080p, 720p o 480p. Se conserva la proporción y sólo se reduce; los límites se aplican al ancho y alto indicados.
- **FPS**: originales, máximo 30 o máximo 24. No se aumenta la frecuencia de un original menor.
- **Audio**: AAC a 192, 128, 96 o 64 kbps, o sin audio. Se usa la primera pista de audio, si existe.

Para un bucle largo y más pequeño: elige **Duración rápida**, activa **Comprimir el clip una vez**, escoge **Equilibrado** o **Archivo pequeño** y ajusta la duración. Verás dos fases: comprimir el clip y repetirlo. Puedes cancelar cualquiera de ellas; los archivos temporales se eliminan. Se necesita espacio para el clip temporal y para el resultado.

El resultado se guarda en MP4. La copia directa conserva la calidad original; recodificar puede perder calidad. Un perfil no garantiza que cualquier archivo ya comprimido vaya a reducirse. El archivo final ocupa espacio proporcional a la duración aunque repita el mismo contenido. La estimación por bitrate incluye audio y es orientativa; en modo de calidad no se puede conocer el tamaño exacto de antemano. El original no se modifica.

La duración final es aproximada: los límites de los fotogramas y las diferencias entre audio y vídeo pueden causar pequeñas variaciones. Si la copia directa falla por un formato incompatible con MP4, activa la compresión previa y selecciona H.264.

## Requisitos

- Windows 10 u 11 con .NET Framework 4.5 o posterior.
- FFmpeg se detecta junto a la aplicación, en su carpeta `bin`, en el `PATH` del proceso, del usuario y del sistema, en los enlaces de WinGet y en la copia descargada por LooperMaster. Se comprueba ejecutando `ffmpeg -version`; una copia dañada no impide buscar otra. Para recodificar por CPU se necesitan `libx264` y `aac`; HEVC por CPU necesita `libx265`.
- Para AMD: una GPU compatible, su controlador y FFmpeg con `h264_amf` o `hevc_amf`, según el códec.
- Para NVIDIA: una GPU compatible con NVENC, su controlador y FFmpeg con `h264_nvenc` o `hevc_nvenc`, según el códec. No es necesario instalar el SDK para usar la aplicación. Consulta la [documentación de NVIDIA y FFmpeg](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/ffmpeg-with-nvidia-gpu/index.html). La disponibilidad de H.264 y HEVC puede ser distinta. Se acelera la codificación; los filtros de resolución y fps siguen ejecutándose por CPU. Intel Quick Sync no está integrado.
- `ffprobe.exe` es opcional si ya tienes FFmpeg, pero permite mostrar la duración original y estimar el tamaño del modo rápido. La descarga automática incluye ambas herramientas. No se descarga nada si FFmpeg ya funciona, aunque falte FFprobe.

## Descarga automática de FFmpeg

Si no se encuentra una copia funcional, LooperMaster consulta directamente las publicaciones de [FFmpeg-Builds](https://github.com/FFmpeg-Builds/FFmpeg-Builds), repositorio de compilaciones de Windows enlazado desde la [página oficial de FFmpeg](https://ffmpeg.org/download.html). El proyecto FFmpeg distribuye código fuente; los ejecutables de Windows los proporcionan los proyectos que enlaza esa página.

Se utiliza la compilación estable GPL más reciente disponible para Windows x64 o ARM64, que incluye x264 y x265. La descarga automática necesita conexión a Internet y Windows de 64 bits; las compilaciones del proveedor requieren Windows 10 22H2 o posterior. En Windows de 32 bits puedes colocar una compilación compatible junto al programa.

El progreso aparece en la barra inferior. Antes de instalar, se comprueba el SHA-256 publicado por el repositorio y se verifica que FFmpeg y FFprobe se puedan ejecutar. Se guardan en `%LOCALAPPDATA%\LooperMaster\ffmpeg`, junto a su licencia y un archivo `ORIGEN.txt` con el enlace y la suma de verificación. No hacen falta permisos de administrador ni cambios en el `PATH`. Las siguientes aperturas reutilizan esta copia sin descargarla de nuevo.

Puedes pulsar **Cancelar** durante la preparación. Si la conexión falla o cancelas, usa **Reintentar FFmpeg**; se vuelve a comprobar si ya hay una instalación disponible. Los archivos parciales se eliminan y una descarga fallida no sustituye una instalación anterior. Cerrar la ventana durante la descarga también la cancela y limpia sus archivos temporales.

## Código fuente

El programa está escrito en C# y utiliza Windows Forms, con un tema propio y sin bibliotecas de interfaz externas. Para recompilarlo, cierra la aplicación y ejecuta `Compilar.bat` en Windows con .NET Framework instalado. El código de procesamiento está en `LooperMaster.cs`; la ventana y los controles se encuentran en `Ventana.cs` e `Interfaz.cs`. `FfmpegSetup.cs` detecta y descarga las herramientas, y `Preparacion.cs` conecta la preparación con la ventana.

Para ejecutar las pruebas locales, abre PowerShell en esta carpeta y ejecuta `./tests/Probar.ps1`. Las pruebas necesitan FFmpeg y FFprobe; crean vídeos de ejemplo y los eliminan al terminar.

`./tests/ProbarFFmpeg.ps1` prueba el detector y la descarga con respuestas locales, sin modificar el `PATH` ni la instalación del usuario. Comprueba instalación existente, caché, SHA-256, paquetes inválidos, cancelación, errores y reintento. `./tests/ProbarFFmpeg.ps1 -DescargaReal` comprueba además una descarga del repositorio y su reutilización sin conexión, dentro de una carpeta temporal que se elimina al terminar.

Las pruebas comprueban CPU/GPU, detección automática de AMD y NVIDIA, prioridad cuando ambos están disponibles, compatibilidad por códec, bitrate, reducción de tamaño medida, HEVC, resolución, fps, audio, copia directa, cancelación de ambas fases, una hora de vídeo y escalado. Las exportaciones reales con cada GPU sólo se ejecutan si el códec supera la detección. También se comprueba el bloqueo sin GPU y el manejo de errores sin cambiar a CPU. La revisión de interfaz comprueba que sólo existen dos opciones de motor, varias anchuras, ausencia de desplazamiento horizontal y acceso permanente a los botones de exportación.
