using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LooperMaster
{
    internal static class FfmpegSetupTests
    {
        private static readonly string AssetName = "ffmpeg-n99.0-latest-" + FfmpegSetup.DownloadPlatform + "-gpl-99.0.zip";
        private static readonly string AssetUrl = FfmpegSetup.RepositoryUrl + "/releases/download/test/" + AssetName;

        private static int Main(string[] args)
        {
            // A copy of this small executable acts as each tool in the downloaded test archive.
            if (args.Length == 1 && args[0] == "-version")
            {
                Console.WriteLine(Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location) + " version fixture");
                return 0;
            }
            if (args.Length < 1) return 1;
            string root = Path.Combine(Path.GetFullPath(args[0]), ".LooperMaster-ffmpeg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                if (args.Length == 2 && args[1] == "--real-download") RealDownload(root).GetAwaiter().GetResult();
                else Run(root).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { Directory.Delete(root, true); }
        }

        private static async Task Run(string root)
        {
            string valid = Path.Combine(root, "instalado");
            string invalid = Path.Combine(root, "roto");
            Directory.CreateDirectory(valid);
            Directory.CreateDirectory(invalid);
            File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(valid, "ffmpeg.exe"));
            File.WriteAllText(Path.Combine(invalid, "ffmpeg.exe"), "No es un ejecutable");
            string cache = Path.Combine(root, "cache", "ffmpeg");
            TestHandler offline = new TestHandler(delegate { throw new InvalidOperationException("No debe acceder a la red si FFmpeg ya existe."); });
            using (HttpClient client = new HttpClient(offline))
            {
                FfmpegTools existing = await FfmpegSetup.EnsureAsync(cache, new[] { invalid, valid }, client, null, CancellationToken.None);
                Require(existing.Ffmpeg == Path.Combine(valid, "ffmpeg.exe") && existing.Ffprobe == null, "No reutilizó FFmpeg sin FFprobe.");
                Require(offline.Requests == 0 && !Directory.Exists(cache), "Una instalación existente provocó una descarga o escritura.");
            }
            Console.WriteLine("OK: detecta FFmpeg funcional, ignora copias rotas y no descarga aunque falte FFprobe.");

            byte[] archive = MakeArchive(true, false);
            string digest = Hash(archive);
            List<SetupProgress> reports = new List<SetupProgress>();
            using (HttpClient client = new HttpClient(DownloadHandler(archive, digest)))
            {
                FfmpegTools installed = await FfmpegSetup.EnsureAsync(cache, new string[0], client, new InlineProgress(reports.Add), CancellationToken.None);
                Require(File.Exists(installed.Ffmpeg) && File.Exists(installed.Ffprobe), "No instaló ambas herramientas.");
                Require(File.Exists(Path.Combine(cache, "LICENSE.txt")), "No conservó la licencia del paquete.");
                Require(File.ReadAllText(Path.Combine(cache, "ORIGEN.txt")).Contains(AssetUrl), "No conservó el origen de la descarga.");
                Require(Directory.GetFiles(cache, "*.zip").Length == 0 && !File.Exists(Path.Combine(root, "outside.txt")), "La extracción dejó el ZIP o escribió fuera de destino.");
                Require(reports.Any(item => item.Percentage.HasValue), "Falta progreso de descarga.");
            }
            AssertClean(Path.GetDirectoryName(cache));
            using (HttpClient client = new HttpClient(offline))
            {
                FfmpegTools cached = await FfmpegSetup.EnsureAsync(cache, new string[0], client, null, CancellationToken.None);
                Require(cached.Ffmpeg == Path.Combine(cache, "ffmpeg.exe") && offline.Requests == 0, "Volvió a descargar una instalación guardada.");
            }
            Console.WriteLine("OK: descarga, verifica, instala FFmpeg/FFprobe y reutiliza la copia guardada sin red.");

            string brokenCache = Path.Combine(root, "fallos", "ffmpeg");
            Directory.CreateDirectory(brokenCache);
            File.WriteAllText(Path.Combine(brokenCache, "conservar.txt"), "Instalación previa");
            await ExpectFailure<InvalidDataException>(brokenCache, DownloadHandler(archive, new string('0', 64)), CancellationToken.None);
            Require(File.Exists(Path.Combine(brokenCache, "conservar.txt")), "Un hash incorrecto modificó la instalación previa.");
            Require(!File.Exists(Path.Combine(brokenCache, "ffmpeg.exe")), "Publicó una descarga corrupta.");
            byte[] incomplete = MakeArchive(false, false);
            await ExpectFailure<InvalidDataException>(brokenCache, DownloadHandler(incomplete, Hash(incomplete)), CancellationToken.None);
            byte[] badTools = MakeArchive(true, true);
            await ExpectFailure<InvalidDataException>(brokenCache, DownloadHandler(badTools, Hash(badTools)), CancellationToken.None);
            Require(File.Exists(Path.Combine(brokenCache, "conservar.txt")), "Un paquete inválido modificó la instalación previa.");
            Console.WriteLine("OK: rechaza SHA-256 incorrecto, paquetes incompletos y ejecutables inválidos sin publicar archivos parciales.");

            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                TestHandler canceled = DownloadHandler(archive, digest);
                canceled.CancelDownload = cancellation;
                await ExpectFailure<OperationCanceledException>(brokenCache, canceled, cancellation.Token);
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await ExpectFailure<OperationCanceledException>(brokenCache, offline, cancellation.Token);
            }
            await ExpectFailure<HttpRequestException>(brokenCache,
                new TestHandler(delegate { return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); }), CancellationToken.None);
            using (HttpClient client = new HttpClient(DownloadHandler(archive, digest)))
                await FfmpegSetup.EnsureAsync(brokenCache, new string[0], client, null, CancellationToken.None);
            Require(File.Exists(Path.Combine(brokenCache, "ffmpeg.exe")), "No permitió reintentar después de un fallo.");
            AssertClean(Path.GetDirectoryName(brokenCache));
            Console.WriteLine("OK: cancelación y errores de conexión limpian temporales y permiten reintentar.");

            string rejectedCache = Path.Combine(root, "origen", "ffmpeg");
            TestHandler unexpected = new TestHandler(delegate { return JsonResponse(Metadata(digest, "https://example.com/ffmpeg.zip")); });
            await ExpectFailure<InvalidDataException>(rejectedCache, unexpected, CancellationToken.None);
            Require(unexpected.Requests == 1 && !Directory.Exists(rejectedCache), "Aceptó una descarga de un origen ajeno al repositorio.");
            Console.WriteLine("OK: rechaza enlaces de descarga ajenos al repositorio original de las compilaciones.");
        }

        private static async Task RealDownload(string root)
        {
            string cache = Path.Combine(root, "ffmpeg");
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LooperMaster/1.2");
                FfmpegTools tools = await FfmpegSetup.EnsureAsync(cache, new string[0], client,
                    new InlineProgress(item => Console.WriteLine(item.Message)), CancellationToken.None);
                Require(await FfmpegSetup.CanRunAsync(tools.Ffmpeg, CancellationToken.None), "La descarga real de FFmpeg no funciona.");
                Require(await FfmpegSetup.CanRunAsync(tools.Ffprobe, CancellationToken.None), "La descarga real de FFprobe no funciona.");
                Console.WriteLine(File.ReadAllText(Path.Combine(cache, "ORIGEN.txt")));
                TestHandler offline = new TestHandler(delegate { throw new InvalidOperationException("La copia real debe reutilizarse sin red."); });
                using (HttpClient cachedClient = new HttpClient(offline))
                    await FfmpegSetup.EnsureAsync(cache, new string[0], cachedClient, null, CancellationToken.None);
                Require(offline.Requests == 0, "La segunda apertura accedió a la red.");
                Console.WriteLine("OK: descarga real del repositorio, SHA-256, ejecutables funcionales y reutilización sin red.");
            }
        }

        private static async Task ExpectFailure<T>(string cache, TestHandler handler, CancellationToken cancellation) where T : Exception
        {
            using (HttpClient client = new HttpClient(handler, false))
            {
                try { await FfmpegSetup.EnsureAsync(cache, new string[0], client, null, cancellation); }
                catch (T) { AssertClean(Path.GetDirectoryName(cache)); return; }
            }
            throw new InvalidOperationException("Se esperaba " + typeof(T).Name + ".");
        }

        private static byte[] MakeArchive(bool includeProbe, bool invalidTools)
        {
            using (MemoryStream data = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(data, ZipArchiveMode.Create, true))
                {
                    byte[] executable = invalidTools ? Encoding.UTF8.GetBytes("Archivo inválido") : File.ReadAllBytes(Assembly.GetExecutingAssembly().Location);
                    AddEntry(zip, "ffmpeg-test/bin/ffmpeg.exe", executable);
                    if (includeProbe) AddEntry(zip, "ffmpeg-test/bin/ffprobe.exe", executable);
                    AddEntry(zip, "ffmpeg-test/LICENSE.txt", Encoding.UTF8.GetBytes("Licencia de prueba"));
                    AddEntry(zip, "ffmpeg-test/../../outside.txt", Encoding.UTF8.GetBytes("No extraer"));
                }
                return data.ToArray();
            }
        }

        private static void AddEntry(ZipArchive zip, string name, byte[] bytes)
        {
            using (Stream output = zip.CreateEntry(name).Open()) output.Write(bytes, 0, bytes.Length);
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static string Metadata(string digest, string url)
        {
            return "{\"assets\":[{\"name\":\"ffmpeg-master-latest-" + FfmpegSetup.DownloadPlatform + "-gpl.zip\"}," +
                "{\"name\":\"" + AssetName + "\",\"browser_download_url\":\"" + url + "\",\"digest\":\"sha256:" + digest + "\"}]}";
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static TestHandler DownloadHandler(byte[] archive, string digest)
        {
            return new TestHandler(delegate(HttpRequestMessage request)
            {
                if (request.RequestUri.Host == "api.github.com") return JsonResponse(Metadata(digest, AssetUrl));
                Require(request.RequestUri.AbsoluteUri == AssetUrl, "Descargó una variante incorrecta.");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            });
        }

        private static void AssertClean(string parent)
        {
            if (!Directory.Exists(parent)) return;
            Require(Directory.GetDirectories(parent, ".ffmpeg-download-*").Length == 0 &&
                Directory.GetDirectories(parent, ".ffmpeg-backup-*").Length == 0, "Quedaron directorios temporales de instalación.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class InlineProgress : IProgress<SetupProgress>
        {
            private readonly Action<SetupProgress> action;
            public InlineProgress(Action<SetupProgress> action) { this.action = action; }
            public void Report(SetupProgress value) { action(value); }
        }

        private sealed class TestHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> response;
            public int Requests;
            public CancellationTokenSource CancelDownload;
            public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> response) { this.response = response; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Requests++;
                HttpResponseMessage result = response(request);
                if (CancelDownload != null && request.RequestUri.Host != "api.github.com")
                    result.Content = new StreamContent(new CancelingStream(CancelDownload));
                return Task.FromResult(result);
            }
        }

        private sealed class CancelingStream : MemoryStream
        {
            private readonly CancellationTokenSource cancellation;
            public CancelingStream(CancellationTokenSource cancellation) : base(new byte[256]) { this.cancellation = cancellation; }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            {
                int read = Read(buffer, offset, Math.Min(16, count));
                cancellation.Cancel();
                return Task.FromResult(read);
            }
        }
    }
}
