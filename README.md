# LooperMaster

Aplicación de Windows para crear vídeos en bucle con FFmpeg.

- Repite un vídeo un número de veces o hasta alcanzar una duración.
- Permite elegir CPU o GPU, con detección automática de AMD AMF y NVIDIA NVENC.
- Comprime el clip una vez y lo repite para producir bucles largos.
- Incluye perfiles de calidad, bitrate manual, H.264, HEVC, resolución, fps y audio.
- Busca FFmpeg instalado y ofrece descarga automática verificada con SHA-256.

## Uso

Descarga el ZIP de la sección Releases de GitHub, extráelo y abre `LooperMaster.exe`. Añade un vídeo, configura el bucle y pulsa **Crear vídeo**.

Requiere Windows 10 u 11 con .NET Framework 4.5 o posterior. La descarga automática de FFmpeg requiere Internet y Windows x64 o ARM64 compatible con las compilaciones del proveedor. Una GPU compatible y sus controladores permiten activar la opción GPU.

La guía completa explica los perfiles, los requisitos y el uso de FFmpeg. [Guía de usuario](LEEME.md)

## Compilar y probar

Desde PowerShell en la carpeta del proyecto se pueden ejecutar estos comandos.

```powershell
.\Compilar.bat
.\tests\Probar.ps1
.\tests\ProbarFFmpeg.ps1
.\scripts\PrepararRelease.ps1
```

Las pruebas de exportación necesitan `ffmpeg.exe` y `ffprobe.exe` en el PATH. Las pruebas de descarga utilizan respuestas locales. El script de release compila en una carpeta temporal, ejecuta ambas suites y genera el ZIP y las sumas SHA-256 en `dist`.

La publicación está preparada mediante GitHub Actions. [Procedimiento de release](docs/RELEASE.md)

## Componentes externos

FFmpeg se obtiene por separado desde las compilaciones enlazadas por su proyecto. La aplicación conserva la licencia y el origen de la descarga en su caché. [Avisos de componentes externos](THIRD_PARTY_NOTICES.md)
