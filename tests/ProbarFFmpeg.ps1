param([switch] $DescargaReal)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'No se encontro el compilador de .NET Framework.' }
$testExe = Join-Path ([System.IO.Path]::GetTempPath()) ('LooperMaster-ffmpeg-tests-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /optimize+ /codepage:65001 /main:LooperMaster.FfmpegSetupTests `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
        /reference:System.Net.Http.dll /reference:System.Runtime.Serialization.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
        "/out:$testExe" (Join-Path $projectRoot 'LooperMaster.cs') (Join-Path $projectRoot 'Interfaz.cs') (Join-Path $projectRoot 'Ventana.cs') `
        (Join-Path $projectRoot 'FfmpegSetup.cs') (Join-Path $projectRoot 'Preparacion.cs') (Join-Path $PSScriptRoot 'FfmpegSetupTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron compilar las pruebas de FFmpeg.' }
    if ($DescargaReal) { & $testExe $projectRoot --real-download }
    else { & $testExe $projectRoot }
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas de FFmpeg fallaron.' }
}
finally {
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe -Force }
}
