using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LooperMaster
{
    internal sealed class FfmpegTools
    {
        public string Ffmpeg;
        public string Ffprobe;
    }

    internal sealed class SetupProgress
    {
        public string Message;
        public int? Percentage;
    }

    internal static class FfmpegSetup
    {
        // This Windows build repository is linked from https://ffmpeg.org/download.html.
        internal const string RepositoryUrl = "https://github.com/FFmpeg-Builds/FFmpeg-Builds";
        private const string ReleaseApi = "https://api.github.com/repos/FFmpeg-Builds/FFmpeg-Builds/releases/latest";

        public static string InstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LooperMaster", "ffmpeg"); }
        }

        public static async Task<FfmpegTools> EnsureAsync(IProgress<SetupProgress> progress, CancellationToken cancellation)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LooperMaster/" + typeof(FfmpegSetup).Assembly.GetName().Version.ToString(3));
                return await EnsureAsync(InstallDirectory, MediaTools.ExecutableDirectories(null), client, progress, cancellation).ConfigureAwait(false);
            }
        }

        // Explicit paths and HTTP client also allow testing without changing PATH or the user's installation.
        internal static async Task<FfmpegTools> EnsureAsync(string installDirectory, IEnumerable<string> searchDirectories,
            HttpClient client, IProgress<SetupProgress> progress, CancellationToken cancellation)
        {
            string[] directories = searchDirectories.Concat(new[] { installDirectory }).ToArray();
            Report(progress, "Buscando FFmpeg en tu equipo…", null);
            FfmpegTools installed = await Task.Run(() => FindInstalledAsync(directories, cancellation), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (installed != null) return installed;
            if (!Environment.Is64BitOperatingSystem)
                throw new NotSupportedException("La descarga automática necesita Windows de 64 bits. En Windows de 32 bits, coloca una versión compatible de ffmpeg.exe junto al programa.");

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string parent = Path.GetDirectoryName(Path.GetFullPath(installDirectory));
            Directory.CreateDirectory(parent);
            // Two windows must not publish or replace the same installation at the same time.
            using (FileStream installationLock = new FileStream(Path.Combine(parent, ".ffmpeg-install.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(15));
                CancellationToken token = timeout.Token;
                string staging = Path.Combine(parent, ".ffmpeg-download-" + Guid.NewGuid().ToString("N"));
                string backup = Path.Combine(parent, ".ffmpeg-backup-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Report(progress, "FFmpeg no está instalado. Consultando su descarga…", null);
                    ReleaseAsset asset = await GetAssetAsync(client, token).ConfigureAwait(false);
                    Directory.CreateDirectory(staging);
                    string archivePath = Path.Combine(staging, "download.zip");
                    using (FileStream archive = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
                    {
                        await DownloadAsync(client, asset, archive, progress, token).ConfigureAwait(false);
                        archive.Position = 0;
                        Report(progress, "Extrayendo FFmpeg y FFprobe…", null);
                        await Task.Run(() => ExtractArchive(archive, staging, token), token).ConfigureAwait(false);
                    }
                    File.Delete(archivePath);
                    Report(progress, "Comprobando FFmpeg y FFprobe…", null);
                    string ffmpeg = Path.Combine(staging, "ffmpeg.exe");
                    string ffprobe = Path.Combine(staging, "ffprobe.exe");
                    if (!await CanRunAsync(ffmpeg, token).ConfigureAwait(false) || !await CanRunAsync(ffprobe, token).ConfigureAwait(false))
                        throw new InvalidDataException("La descarga no contiene una versión de FFmpeg y FFprobe compatible con este equipo.");
                    File.WriteAllText(Path.Combine(staging, "ORIGEN.txt"), "Compilaciones enlazadas por https://ffmpeg.org/download.html\r\n" +
                        asset.DownloadUrl + "\r\n" + asset.Digest + "\r\n", Encoding.UTF8);
                    token.ThrowIfCancellationRequested();
                    if (Directory.Exists(installDirectory)) Directory.Move(installDirectory, backup);
                    try { Directory.Move(staging, installDirectory); }
                    catch
                    {
                        if (Directory.Exists(backup)) Directory.Move(backup, installDirectory);
                        throw;
                    }
                    DeleteTemporaryDirectory(backup);
                    return new FfmpegTools { Ffmpeg = Path.Combine(installDirectory, "ffmpeg.exe"), Ffprobe = Path.Combine(installDirectory, "ffprobe.exe") };
                }
                catch (OperationCanceledException)
                {
                    if (!cancellation.IsCancellationRequested)
                        throw new TimeoutException("La descarga de FFmpeg ha tardado demasiado. Comprueba la conexión y vuelve a intentarlo.");
                    throw;
                }
                finally { DeleteTemporaryDirectory(staging); }
            }
        }

        private static async Task<FfmpegTools> FindInstalledAsync(string[] directories, CancellationToken cancellation)
        {
            foreach (string ffmpeg in MediaTools.FindExecutables("ffmpeg.exe", directories))
            {
                if (!await CanRunAsync(ffmpeg, cancellation).ConfigureAwait(false)) continue;
                string ffprobe = null;
                foreach (string candidate in MediaTools.FindExecutables("ffprobe.exe", new[] { Path.GetDirectoryName(ffmpeg) }.Concat(directories)))
                {
                    if (await CanRunAsync(candidate, cancellation).ConfigureAwait(false)) { ffprobe = candidate; break; }
                }
                // FFprobe is optional: an existing FFmpeg must never trigger a download just because FFprobe is missing.
                return new FfmpegTools { Ffmpeg = ffmpeg, Ffprobe = ffprobe };
            }
            return null;
        }

        internal static async Task<bool> CanRunAsync(string executable, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            using (Process process = MediaTools.CreateProcess(executable, new[] { "-version" }))
            {
                bool started = false;
                try
                {
                    process.Start();
                    started = true;
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    Task finished = Task.WhenAll(output, error, Task.Run((Action)process.WaitForExit));
                    if (await Task.WhenAny(finished, Task.Delay(5000, cancellation)).ConfigureAwait(false) != finished)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        return false;
                    }
                    await finished.ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    string banner = Path.GetFileNameWithoutExtension(executable) + " version ";
                    return process.ExitCode == 0 && (output.Result.StartsWith(banner, StringComparison.OrdinalIgnoreCase) ||
                        error.Result.StartsWith(banner, StringComparison.OrdinalIgnoreCase));
                }
                catch (System.ComponentModel.Win32Exception) { return false; }
                catch (InvalidOperationException) { return false; }
                catch (IOException) { return false; }
                finally
                {
                    if (started)
                    {
                        try { if (!process.HasExited) process.Kill(); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
            }
        }

        internal static string DownloadPlatform
        {
            get
            {
                string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                return String.Equals(architecture, "ARM64", StringComparison.OrdinalIgnoreCase) ? "winarm64" : "win64";
            }
        }

        private static async Task<ReleaseAsset> GetAssetAsync(HttpClient client, CancellationToken cancellation)
        {
            using (HttpResponseMessage response = await client.GetAsync(ReleaseApi, cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("No se pudo consultar el repositorio de FFmpeg (HTTP " + (int)response.StatusCode + "). Vuelve a intentarlo más tarde.");
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                ReleaseInfo release;
                using (MemoryStream data = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    release = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(data);
                ReleaseAsset selected = null;
                Version newest = new Version(0, 0);
                string pattern = @"^ffmpeg-n(\d+\.\d+(?:\.\d+)?)-latest-" + DownloadPlatform + @"-gpl(?:-\1)?\.zip$";
                foreach (ReleaseAsset asset in release.Assets ?? new ReleaseAsset[0])
                {
                    Match match = Regex.Match(asset.Name ?? "", pattern);
                    Version version;
                    if (match.Success && Version.TryParse(match.Groups[1].Value, out version) && version > newest)
                    {
                        newest = version;
                        selected = asset;
                    }
                }
                if (selected == null) throw new InvalidDataException("No se encontró una compilación estable de FFmpeg para " + DownloadPlatform + " en el repositorio.");
                Uri download;
                if (!Uri.TryCreate(selected.DownloadUrl, UriKind.Absolute, out download) ||
                    !download.AbsoluteUri.StartsWith(RepositoryUrl + "/releases/download/", StringComparison.Ordinal))
                    throw new InvalidDataException("El repositorio devolvió un enlace de descarga inesperado.");
                if (!Regex.IsMatch(selected.Digest ?? "", @"^sha256:[a-fA-F0-9]{64}$"))
                    throw new InvalidDataException("El repositorio no publicó una suma SHA-256 válida para verificar FFmpeg.");
                return selected;
            }
        }

        private static async Task DownloadAsync(HttpClient client, ReleaseAsset asset, Stream output,
            IProgress<SetupProgress> progress, CancellationToken cancellation)
        {
            Report(progress, "Descargando FFmpeg y FFprobe…", 0);
            using (HttpResponseMessage response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("No se pudo descargar FFmpeg (HTTP " + (int)response.StatusCode + ").");
                long? total = response.Content.Headers.ContentLength;
                long downloaded = 0;
                Stopwatch throttle = Stopwatch.StartNew();
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                        sha.TransformBlock(buffer, 0, read, buffer, 0);
                        downloaded += read;
                        if (throttle.ElapsedMilliseconds < 150) continue;
                        int? percent = total.HasValue && total.Value > 0 ? (int?)Math.Min(100, downloaded * 100 / total.Value) : null;
                        Report(progress, "Descargando FFmpeg y FFprobe · " + (downloaded / 1048576) + " MB…", percent);
                        throttle.Restart();
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (total.HasValue && downloaded != total.Value) throw new InvalidDataException("La descarga de FFmpeg quedó incompleta.");
                    sha.TransformFinalBlock(buffer, 0, 0);
                    string digest = "sha256:" + BitConverter.ToString(sha.Hash).Replace("-", "");
                    if (!String.Equals(digest, asset.Digest, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("La verificación SHA-256 de FFmpeg falló. Vuelve a descargarlo.");
                }
            }
        }

        private static void ExtractArchive(Stream verifiedArchive, string destination, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            using (ZipArchive archive = new ZipArchive(verifiedArchive, ZipArchiveMode.Read, true))
            {
                foreach (string tool in new[] { "ffmpeg.exe", "ffprobe.exe" })
                {
                    ZipArchiveEntry[] entries = archive.Entries.Where(entry => entry.FullName.Replace('\\', '/').EndsWith("/bin/" + tool, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (entries.Length != 1) throw new InvalidDataException("El paquete de FFmpeg no contiene " + tool + " de forma inequívoca.");
                    Extract(entries[0], Path.Combine(destination, tool), cancellation);
                }
                ZipArchiveEntry license = archive.Entries.FirstOrDefault(entry => String.Equals(entry.Name, "LICENSE.txt", StringComparison.OrdinalIgnoreCase));
                if (license != null) Extract(license, Path.Combine(destination, "LICENSE.txt"), cancellation);
            }
        }

        private static void Extract(ZipArchiveEntry entry, string destination, CancellationToken cancellation)
        {
            // Map only the expected filenames; never use paths supplied by the ZIP as output paths.
            if (entry.Length > 536870912) throw new InvalidDataException("El paquete de FFmpeg contiene un archivo demasiado grande.");
            using (Stream input = entry.Open())
            using (FileStream output = new FileStream(destination, FileMode.CreateNew))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                }
            }
        }

        private static void Report(IProgress<SetupProgress> progress, string message, int? percentage)
        {
            if (progress != null) progress.Report(new SetupProgress { Message = message, Percentage = percentage });
        }

        private static void DeleteTemporaryDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [DataContract]
        private sealed class ReleaseInfo
        {
            [DataMember(Name = "assets")] public ReleaseAsset[] Assets { get; set; }
        }

        [DataContract]
        private sealed class ReleaseAsset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
        }
    }
}
