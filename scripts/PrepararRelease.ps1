param([switch] $OmitirPruebas)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -Raw -LiteralPath (Join-Path $root 'LooperMaster.cs')
if ($source -notmatch 'AssemblyVersion\("(\d+\.\d+\.\d+)\.0"\)') { throw 'No se pudo leer la versión.' }
$version = $Matches[1]
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'La etiqueta y la versión del código deben coincidir.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Falta el compilador de .NET Framework.' }
if (-not $OmitirPruebas) {
    & (Join-Path $root 'tests\Probar.ps1')
    & (Join-Path $root 'tests\ProbarFFmpeg.ps1')
}
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$staging = Join-Path $dist ('.release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $sources = @('LooperMaster.cs','Interfaz.cs','Ventana.cs','FfmpegSetup.cs','Preparacion.cs') | ForEach-Object { Join-Path $root $_ }
    $references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Runtime.Serialization.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object { '/reference:' + $_ }
    $executable = Join-Path $staging 'LooperMaster.exe'
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /warnaserror+ /utf8output /codepage:65001 "/win32manifest:$(Join-Path $root 'app.manifest')" "/out:$executable" @references @sources
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del release.' }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion -ne "$version.0") { throw 'Versión del ejecutable incorrecta.' }
    foreach ($file in @('LEEME.md','RELEASE_NOTES.md','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $staging }
    $zip = Join-Path $dist "LooperMaster-v$version-windows.zip"
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.Encoding]::ASCII)
    Write-Output "Preparado $zip"
    Write-Output "SHA-256 $hash"
}
finally {
    $resolved = [IO.Path]::GetFullPath($staging)
    if ([IO.Path]::GetDirectoryName($resolved) -eq [IO.Path]::GetFullPath($dist) -and [IO.Path]::GetFileName($resolved).StartsWith('.release-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
