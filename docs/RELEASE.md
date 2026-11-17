# Publicar LooperMaster

## Preparación local

`scripts/PrepararRelease.ps1` compila y prueba el código actual. El resultado es `dist/LooperMaster-v1.3.0-windows.zip` y `dist/SHA256SUMS.txt`.

El ZIP incluye el ejecutable, la guía de usuario, las notas y los avisos de componentes externos. Las copias antiguas y los archivos temporales se excluyen mediante `.gitignore`.

La versión de referencia se lee de `AssemblyVersion` en `LooperMaster.cs`. Actualiza también `AssemblyFileVersion` y `RELEASE_NOTES.md` al preparar otra versión.

## GitHub

1. Crea o elige el repositorio de destino y sube el código a su rama principal.
2. Comprueba que pase el workflow Windows.
3. Crea y sube la etiqueta `v1.3.0` sobre el commit validado.
4. El workflow genera un release en borrador con el ZIP y sus sumas SHA-256.
5. Revisa los archivos y las notas del borrador y pulsa Publish release.

La CLI local puede configurarse con `gh auth login`. También se pueden cargar el ZIP y las sumas manualmente desde la página de Releases.

## Estado de la preparación local

La carpeta se prepara con fuentes, pruebas y workflows. Para completar la publicación es necesario configurar el repositorio remoto y tener acceso de escritura a GitHub.
