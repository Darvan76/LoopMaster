# LooperMaster v1.3.0

## Cambios

- Interfaz oscura redimensionable, con controles agrupados y exportación siempre accesible.
- Selección CPU/GPU con detección automática de AMD AMF y NVIDIA NVENC para H.264 y HEVC.
- Perfiles de compresión, bitrate manual, límites de resolución y fps, y opciones de audio.
- Compresión del clip una sola vez antes de repetirlo para crear vídeos largos.
- Detección de FFmpeg instalado y descarga automática con verificación SHA-256, caché y cancelación.
- Uso de memoria acotado para los diagnósticos de FFmpeg y actualización del progreso por porcentaje.

## Instalación

Descarga `LooperMaster-v1.3.0-windows.zip`, extráelo y abre `LooperMaster.exe`. Consulta `LEEME.md` para los requisitos de Windows y las opciones de compresión.

## Validación y límites

Las pruebas automatizadas cubren exportación CPU y GPU disponible, reproducción repetida de vídeo y audio, HEVC, bitrate, dimensiones impares, cancelación, descarga, integridad y recuperación de errores.

La codificación real AMD se ha comprobado localmente. Las pruebas NVIDIA se ejecutan cuando NVENC está disponible. Este equipo no permite verificar exportaciones NVIDIA reales. GitHub Actions comprueba la exportación por CPU y la lógica de detección.

El ejecutable se distribuye sin firma de código.
