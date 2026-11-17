using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LooperMaster
{
    // Exercises the application's own drop and export handlers without showing a window.
    internal static class SmokeTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 1 || args.Length > 2) { Console.Error.WriteLine("Falta la carpeta de pruebas."); return 1; }
            string temporaryRoot = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
            string testDirectory = Path.Combine(temporaryRoot, ".LooperMaster-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                string diagnostic = new string('x', 16384) + "último error";
                using (StringReader reader = new StringReader(diagnostic))
                    Require(MediaTools.ReadErrorTailAsync(reader).GetAwaiter().GetResult() == diagnostic.Substring(diagnostic.Length - 1500),
                        "Se perdió el diagnóstico final de FFmpeg.");
                string ffmpeg = MediaTools.FindExecutable("ffmpeg.exe", null);
                string ffprobe = MediaTools.FindExecutable("ffprobe.exe", ffmpeg == null ? null : Path.GetDirectoryName(ffmpeg));
                Require(ffmpeg != null && ffprobe != null, "Se necesitan ffmpeg y ffprobe en el PATH.");

                string withAudio = Path.Combine(testDirectory, "vídeo con sonido.mp4");
                string silent = Path.Combine(testDirectory, "sin audio.mkv");
                string tiny = Path.Combine(testDirectory, "vídeo pequeño.mp4");
                string incompatible = Path.Combine(testDirectory, "formato no compatible.avi");
                Run(ffmpeg, new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=24",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "2",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", withAudio });
                Run(ffmpeg, new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=size=319x241:rate=20",
                    "-t", "1.5", "-c:v", "ffv1", silent });
                Run(ffmpeg, new[] { "-v", "error", "-f", "lavfi", "-i", "color=c=blue:size=64x64:rate=2",
                    "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", tiny });
                Run(ffmpeg, new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=10",
                    "-t", "1", "-c:v", "rawvideo", "-pix_fmt", "yuv420p", incompatible });
                byte[] originalHash;
                using (SHA256 hash = SHA256.Create()) originalHash = hash.ComputeHash(File.ReadAllBytes(withAudio));

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm form = new MainForm(false))
                {
                    if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    Task gpuDetection = (Task)Call(form, "PrepareFfmpegAsync");
                    WaitUntil(delegate { return gpuDetection.IsCompleted; }, "detección GPU");
                    gpuDetection.GetAwaiter().GetResult();
                    bool hasAmd = Field<bool>(form, "gpuAvailable");
                    bool hasNvidia = Field<bool>(form, "nvidiaAvailable");
                    bool hasGpu = hasAmd || hasNvidia;
                    string h264Encoder = hasNvidia ? "h264_nvenc" : "h264_amf";
                    Require(Field<RadioButton>(form, "gpuMode").Enabled == hasGpu, "La disponibilidad GPU no coincide con el selector.");
                    RadioButton[] engines = Field<Panel>(form, "engineModes").Controls.OfType<RadioButton>().ToArray();
                    Require(engines.Length == 2 && engines.Any(option => option.Text == "CPU") && engines.Any(option => option.Text == "GPU"),
                        "La interfaz sólo debe ofrecer CPU y GPU.");
                    CheckLayout(form);
                    Console.WriteLine("OK: disposición y texto a " + form.CurrentAutoScaleDimensions.Width + " ppp.");
                    if (args.Length == 2) SavePreview(form, args[1]);

                    Drop(form, withAudio);
                    if (args.Length == 2)
                        SavePreview(form, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),
                            Path.GetFileNameWithoutExtension(args[1]) + "-cargado.png"));
                    Require(Field<NumericUpDown>(form, "count").Value == 3, "El valor inicial debe ser 3.");
                    string audioOutput = Field<string>(form, "outputPath");
                    Export(form);
                    Require(File.Exists(audioOutput), "No se creó el vídeo con sonido.");
                    CheckOutput(ffprobe, audioOutput, 6.0, true);
                    CheckRepeatedVideo(ffmpeg, audioOutput);
                    CheckRepeatedAudio(ffmpeg, audioOutput);
                    Console.WriteLine("OK: tres reproducciones con vídeo y audio; ruta con espacios y caracteres Unicode.");

                    if (hasGpu)
                    {
                        Field<RadioButton>(form, "gpuMode").Checked = true;
                        string gpuOutput = Field<string>(form, "outputPath");
                        Export(form);
                        CheckOutput(ffprobe, gpuOutput, 6.0, true);
                        CheckRepeatedVideo(ffmpeg, gpuOutput);
                        CheckRepeatedAudio(ffmpeg, gpuOutput);
                        string encoderTag = System.Text.Encoding.UTF8.GetString(Run(ffprobe, new[] { "-v", "error", "-select_streams", "v:0",
                            "-show_entries", "stream_tags=encoder", "-of", "default=noprint_wrappers=1", gpuOutput }));
                        Require(encoderTag.Contains(h264Encoder), "El resultado GPU no utiliza el motor detectado: " + h264Encoder);
                        if (args.Length == 2) SavePreview(form, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "vista-gpu.png"));
                        Console.WriteLine("OK: GPU automática (" + h264Encoder + "), H.264, audio y fotogramas repetidos.");
                    }
                    else Console.WriteLine("OMITIDO: GPU compatible no disponible en este equipo.");

                    Field<RadioButton>(form, "durationMode").Checked = true;
                    Require(!Field<RadioButton>(form, "cpuMode").Enabled && !Field<RadioButton>(form, "gpuMode").Enabled,
                        "La copia directa no debe permitir seleccionar un codificador.");
                    Field<NumericUpDown>(form, "durationHours").Value = 0;
                    Require(Field<Label>(form, "progressText").Text == "" && !Field<Button>(form, "showResult").Enabled,
                        "El resultado anterior no debe mostrarse como resultado del nuevo modo.");
                    Require(!Field<NumericUpDown>(form, "count").Enabled && Field<NumericUpDown>(form, "durationSeconds").Enabled,
                        "Los controles no coinciden con el modo rápido.");
                    Require(!Field<Button>(form, "create").Enabled, "La duración cero debe impedir la exportación.");
                    Field<NumericUpDown>(form, "durationSeconds").Value = 7;
                    Require(Field<Button>(form, "create").Enabled, "Una duración válida debe permitir la exportación.");
                    Require(!Field<Label>(form, "estimate").Text.Contains("—"), "Falta la estimación de tamaño.");
                    if (args.Length == 2)
                        SavePreview(form, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),
                            Path.GetFileNameWithoutExtension(args[1]) + "-rapido.png"));
                    string fastOutput = Field<string>(form, "outputPath");
                    Export(form);
                    CheckOutput(ffprobe, fastOutput, 7.0, true);
                    string sourcePacketHash = FirstVideoPacketHash(ffprobe, withAudio);
                    Require(!String.IsNullOrEmpty(sourcePacketHash) && sourcePacketHash == FirstVideoPacketHash(ffprobe, fastOutput),
                        "La copia rápida modificó el vídeo comprimido.");
                    CheckRepeatedAudio(ffmpeg, fastOutput);
                    Console.WriteLine("OK: duración personalizada, vídeo sin recodificar y audio repetido.");

                    Field<NumericUpDown>(form, "durationMinutes").Value = 2;
                    Field<NumericUpDown>(form, "durationSeconds").Value = 0;
                    string minutesOutput = Field<string>(form, "outputPath");
                    Export(form);
                    CheckOutput(ffprobe, minutesOutput, 120.0, true);
                    Console.WriteLine("OK: duración elegida en minutos.");

                    Drop(form, tiny);
                    Field<NumericUpDown>(form, "durationHours").Value = 1;
                    Field<NumericUpDown>(form, "durationMinutes").Value = 0;
                    string hourOutput = Field<string>(form, "outputPath");
                    Export(form);
                    CheckOutput(ffprobe, hourOutput, 3600.0, false);
                    string tinyPacketHash = FirstVideoPacketHash(ffprobe, tiny);
                    Require(!String.IsNullOrEmpty(tinyPacketHash) && tinyPacketHash == FirstVideoPacketHash(ffprobe, hourOutput),
                        "El vídeo largo no se copió sin recodificar.");
                    Console.WriteLine("OK: vídeo de una hora sin audio y más de 1000 repeticiones.");

                    Drop(form, incompatible);
                    Field<NumericUpDown>(form, "durationHours").Value = 0;
                    Field<NumericUpDown>(form, "durationSeconds").Value = 4;
                    string incompatibleOutput = Field<string>(form, "outputPath");
                    Task failed = (Task)Call(form, "StartExportAsync");
                    WaitUntil(delegate { return failed.IsCompleted; }, "formato incompatible");
                    failed.GetAwaiter().GetResult();
                    string failedStatus = Field<Label>(form, "status").Text;
                    Require(failedStatus.Contains("formato no es compatible"), "Falta orientación ante un formato incompatible: " + failedStatus);
                    Require(!File.Exists(incompatibleOutput), "Se publicó un resultado incompatible.");
                    Require(Directory.GetFiles(testDirectory, ".LooperMaster-*.mp4").Length == 0, "Quedó un archivo temporal tras el error.");
                    Console.WriteLine("OK: el modo rápido informa de formatos incompatibles y limpia el archivo temporal.");

                    Field<RadioButton>(form, "repetitionsMode").Checked = true;
                    Field<RadioButton>(form, "cpuMode").Checked = true;
                    Drop(form, silent);
                    Field<NumericUpDown>(form, "count").Value = 4;
                    string silentOutput = Field<string>(form, "outputPath");
                    Export(form);
                    Require(File.Exists(silentOutput), "No se creó el vídeo sin audio.");
                    string details = CheckOutput(ffprobe, silentOutput, 6.0, false);
                    Require(details.Contains("width=320") && details.Contains("height=242"), "Las dimensiones impares no se adaptaron para H.264.");
                    Console.WriteLine("OK: cuatro reproducciones sin audio y con dimensiones impares.");

                    if (hasGpu)
                    {
                        Field<RadioButton>(form, "gpuMode").Checked = true;
                        string oddGpuOutput = Field<string>(form, "outputPath");
                        Export(form);
                        CheckOutput(ffprobe, oddGpuOutput, 6.0, false);
                        Field<RadioButton>(form, "cpuMode").Checked = true;
                        typeof(MainForm).GetField("gpuAvailable", PrivateInstance).SetValue(form, false);
                        typeof(MainForm).GetField("nvidiaAvailable", PrivateInstance).SetValue(form, false);
                        Call(form, "UpdateControls");
                        Require(!Field<RadioButton>(form, "gpuMode").Enabled && Field<RadioButton>(form, "cpuMode").Enabled,
                            "CPU debe seguir disponible cuando no hay GPU.");
                        typeof(MainForm).GetField("gpuAvailable", PrivateInstance).SetValue(form, hasAmd);
                        typeof(MainForm).GetField("nvidiaAvailable", PrivateInstance).SetValue(form, hasNvidia);
                        Call(form, "UpdateControls");
                        Console.WriteLine("OK: GPU sin audio, dimensiones impares y CPU disponible sin GPU.");
                    }

                    Field<NumericUpDown>(form, "count").Value = 1;
                    string singleOutput = Field<string>(form, "outputPath");
                    Export(form);
                    CheckOutput(ffprobe, singleOutput, 1.5, false);
                    Console.WriteLine("OK: una reproducción conserva la duración original.");

                    try { TestCompression(form, ffmpeg, ffprobe, testDirectory, hasGpu, args.Length == 2 ? args[1] : null); }
                    catch (Exception ex) { Console.Error.WriteLine("Fallo en compresión: " + ex); throw; }
                    TestAutomaticGpu(form, ffmpeg, ffprobe, testDirectory);

                    typeof(MainForm).GetField("ffprobe", PrivateInstance).SetValue(form, null);
                    Drop(form, silent);
                    Require(!Field<double?>(form, "inputDuration").HasValue, "La duración debería ser desconocida sin ffprobe.");
                    Field<NumericUpDown>(form, "count").Value = 2;
                    string noProbeOutput = Field<string>(form, "outputPath");
                    Export(form);
                    CheckOutput(ffprobe, noProbeOutput, 3.0, false);
                    Console.WriteLine("OK: exportación sin ffprobe.");

                    Drop(form, withAudio);
                    Field<RadioButton>(form, "durationMode").Checked = true;
                    Field<NumericUpDown>(form, "durationSeconds").Value = 9;
                    Require(Field<Label>(form, "estimate").Text.Contains("—"), "Sin ffprobe no debe mostrarse una estimación falsa.");
                    string noProbeFastOutput = Field<string>(form, "outputPath");
                    Export(form);
                    CheckOutput(ffprobe, noProbeFastOutput, 9.0, true);
                    Console.WriteLine("OK: duración rápida sin ffprobe.");

                    Field<CheckBox>(form, "compressOnce").Checked = true;
                    string noProbeCompressed = Field<string>(form, "outputPath");
                    Export(form);
                    CheckOutput(ffprobe, noProbeCompressed, 9.0, true);
                    Require(Directory.GetFiles(testDirectory, ".LooperMaster-*.mp4").Length == 0, "La compresión sin ffprobe dejó temporales.");
                    Field<CheckBox>(form, "compressOnce").Checked = false;
                    Console.WriteLine("OK: compresión previa sin ffprobe.");

                    Drop(form, tiny);
                    Field<NumericUpDown>(form, "durationHours").Value = 100;
                    Field<NumericUpDown>(form, "durationSeconds").Value = 0;
                    string fastCancelledOutput = Field<string>(form, "outputPath");
                    Task fastCancelled = (Task)Call(form, "StartExportAsync");
                    Call(form, "CancelExport");
                    WaitUntil(delegate { return fastCancelled.IsCompleted; }, "cancelación del modo rápido");
                    fastCancelled.GetAwaiter().GetResult();
                    Require(!File.Exists(fastCancelledOutput), "La copia rápida cancelada dejó un archivo de salida.");
                    Require(Directory.GetFiles(testDirectory, ".LooperMaster-*.mp4").Length == 0, "La copia rápida cancelada dejó un archivo temporal.");
                    Console.WriteLine("OK: cancelación del modo rápido.");

                    Field<RadioButton>(form, "repetitionsMode").Checked = true;
                    Drop(form, withAudio);
                    Field<NumericUpDown>(form, "count").Value = 1000;
                    string cancelledOutput = Field<string>(form, "outputPath");
                    Task task = (Task)Call(form, "StartExportAsync");
                    Call(form, "CancelExport");
                    WaitUntil(delegate { return task.IsCompleted; }, "cancelación");
                    task.GetAwaiter().GetResult();
                    Require(!File.Exists(cancelledOutput), "La cancelación dejó un archivo de salida.");
                    Require(Directory.GetFiles(testDirectory, ".LooperMaster-*.mp4").Length == 0, "La cancelación dejó un archivo temporal.");
                    using (SHA256 hash = SHA256.Create())
                        Require(originalHash.SequenceEqual(hash.ComputeHash(File.ReadAllBytes(withAudio))), "El vídeo original ha sido alterado.");
                    Console.WriteLine("OK: cancelación y limpieza del archivo temporal.");
                }
                using (MainForm smallScreenForm = new MainForm(false))
                {
                    Call(smallScreenForm, "FitToScreen", new Rectangle(0, 0, 1024, 768));
                    Panel viewport = Field<Panel>(smallScreenForm, "scrollHost");
                    Require(smallScreenForm.Width <= 992 && smallScreenForm.Height <= 736 && viewport.AutoScroll,
                        "La ventana no cabe en una pantalla pequeña.");
                    Require(viewport.AutoScrollMinSize.Height > viewport.ClientSize.Height, "Falta desplazamiento para acceder al contenido.");
                    Require(!viewport.HorizontalScroll.Visible, "Una pantalla pequeña no debe necesitar desplazamiento horizontal.");
                    CheckExportBar(smallScreenForm);
                    if (args.Length == 2) SavePreview(smallScreenForm, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "vista-compacta.png"));
                    Console.WriteLine("OK: pantalla pequeña sin recortes horizontales y con la exportación siempre accesible.");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                string normalized = Path.GetFullPath(testDirectory).TrimEnd(Path.DirectorySeparatorChar);
                if (String.Equals(Path.GetDirectoryName(normalized), temporaryRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(normalized).StartsWith(".LooperMaster-tests-", StringComparison.Ordinal))
                {
                    try { Directory.Delete(normalized, true); } catch (IOException) { }
                }
            }
        }

        private static void Drop(MainForm form, string path)
        {
            DataObject data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { path });
            DragEventArgs drag = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            Call(form, "DropEntered", form, drag);
            Require(drag.Effect == DragDropEffects.Copy, "El área no acepta el archivo arrastrado.");
            Call(form, "VideoDropped", form, drag);
            WaitUntil(delegate { return !Field<bool>(form, "probing"); }, "análisis del vídeo");
            Require(String.Equals(Field<string>(form, "inputPath"), path, StringComparison.OrdinalIgnoreCase), "No se seleccionó el vídeo arrastrado.");
        }

        private static void CheckLayout(MainForm form)
        {
            DropZone zone = Field<DropZone>(form, "dropZone");
            Require(zone.Width > 0 && zone.Right <= zone.Parent.ClientSize.Width,
                "El área de arrastre debe adaptarse a la anchura disponible.");
            CheckExportBar(form);
            Size initial = form.ClientSize;
            double scale = form.CurrentAutoScaleDimensions.Width / 96.0;
            foreach (int logicalWidth in new[] { 1060, 920, 710, 640 })
            {
                form.ClientSize = new Size((int)(logicalWidth * scale), (int)(620 * scale));
                form.PerformLayout();
                Panel viewport = Field<Panel>(form, "scrollHost");
                Require(!viewport.HorizontalScroll.Visible, "La distribución necesita desplazamiento horizontal a " + logicalWidth);
                foreach (string name in new[] { "loopPanel", "compressionPanel", "exportPanel" })
                {
                    Panel panel = Field<Panel>(form, name);
                    Require(panel.Right <= panel.Parent.ClientSize.Width + 1 && panel.Bottom <= panel.Parent.ClientSize.Height + 1,
                        "Un grupo de ajustes se sale del contenido: " + name + " " + panel.Bounds + " en " + panel.Parent.ClientSize);
                }
                foreach (string name in new[] { "compressionProfile", "codecChoice", "resolutionChoice", "fpsChoice", "rateChoice", "audioChoice", "videoBitrate" })
                {
                    Control field = Field<Control>(form, name);
                    Require(field.Left >= 0 && field.Right <= field.Parent.ClientSize.Width + 1 && field.Bottom <= field.Parent.ClientSize.Height + 1,
                        "Un campo de compresión queda recortado: " + name);
                }
                CheckExportBar(form);
            }
            form.ClientSize = initial;
            form.PerformLayout();

            CheckTextFits(Field<Label>(form, "dropTitle"));
            CheckTextFits(Field<Label>(form, "dropDetails"));
            CheckTextFits(Field<Button>(form, "chooseVideo"));
            CheckTextFits(Field<RadioButton>(form, "repetitionsMode"));
            CheckTextFits(Field<RadioButton>(form, "durationMode"));
            CheckTextFits(Field<RadioButton>(form, "cpuMode"));
            CheckTextFits(Field<RadioButton>(form, "gpuMode"));
            CheckTextFits(Field<Button>(form, "create"));
            CheckTextFits(Field<Button>(form, "showResult"));
        }

        private static void CheckExportBar(MainForm form)
        {
            Panel footer = Field<Panel>(form, "footer");
            Panel viewport = Field<Panel>(form, "scrollHost");
            Require(footer.Bottom == form.ClientSize.Height && viewport.Bottom <= footer.Top,
                "La barra de exportación debe estar fuera del contenido desplazable.");
            foreach (string name in new[] { "create", "cancel", "showResult" })
            {
                Button button = Field<Button>(form, name);
                Require(button.Parent == footer && footer.ClientRectangle.Contains(button.Bounds),
                    "Un botón de exportación queda fuera de la ventana: " + name);
                CheckTextFits(button);
            }
        }

        private static void CheckTextFits(Control control)
        {
            Size text = TextRenderer.MeasureText(control.Text, control.Font, new Size(Int32.MaxValue, Int32.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Require(text.Height <= control.ClientSize.Height, "El texto no cabe en altura: " + control.Text);
            Require(text.Width <= control.ClientSize.Width - 4, "El texto no cabe en anchura: " + control.Text);
        }

        private static void SetDestination(MainForm form, string path)
        {
            typeof(MainForm).GetField("outputPath", PrivateInstance).SetValue(form, path);
            typeof(MainForm).GetField("destinationCustomized", PrivateInstance).SetValue(form, true);
            Field<TextBox>(form, "destination").Text = path;
        }

        private static string VideoDetails(string ffprobe, string path)
        {
            return System.Text.Encoding.UTF8.GetString(Run(ffprobe, new[] { "-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=codec_name,width,height,avg_frame_rate,bit_rate:stream_tags=encoder", "-of", "default=noprint_wrappers=1", path }));
        }

        private static void TestCompression(MainForm form, string ffmpeg, string ffprobe, string directory, bool hasGpu, string capture)
        {
            string h264Encoder = Field<bool>(form, "nvidiaAvailable") ? "h264_nvenc" : "h264_amf";
            string hevcEncoder = Field<bool>(form, "nvidiaHevcAvailable") ? "hevc_nvenc" : "hevc_amf";
            string source = Path.Combine(directory, "compresión original.mp4");
            Run(ffmpeg, new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=960x540:rate=60",
                "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000", "-t", "4", "-c:v", "libx264",
                "-preset", "ultrafast", "-crf", "10", "-c:a", "aac", source });
            Drop(form, source);
            Field<RadioButton>(form, "repetitionsMode").Checked = true;
            Field<RadioButton>(form, "cpuMode").Checked = true;
            Field<NumericUpDown>(form, "count").Value = 1;
            Field<ComboBox>(form, "rateChoice").SelectedIndex = 3;
            Field<NumericUpDown>(form, "videoBitrate").Value = 3000;
            string high = Path.Combine(directory, "bitrate-alto.mp4");
            SetDestination(form, high);
            Export(form);
            Field<NumericUpDown>(form, "videoBitrate").Value = 500;
            Require(Field<Label>(form, "estimate").Text.Contains("Tamaño orientativo"), "Falta la estimación por bitrate.");
            string low = Path.Combine(directory, "bitrate-bajo.mp4");
            SetDestination(form, low);
            Export(form);
            CheckOutput(ffprobe, low, 4, true);
            Require(new FileInfo(low).Length < new FileInfo(high).Length * 0.75, "Reducir el bitrate no redujo el archivo de prueba.");
            Console.WriteLine("OK: bitrate manual CPU (3000/500 kbps) y reducción de tamaño medida.");

            if (hasGpu)
            {
                Field<RadioButton>(form, "gpuMode").Checked = true;
                string gpu = Path.Combine(directory, "bitrate-gpu.mp4");
                SetDestination(form, gpu);
                Export(form);
                CheckOutput(ffprobe, gpu, 4, true);
                Require(VideoDetails(ffprobe, gpu).Contains(h264Encoder), "El bitrate GPU no usa el motor detectado.");
                Require(new FileInfo(gpu).Length < new FileInfo(high).Length, "El perfil GPU de bajo bitrate no redujo el archivo.");
                Console.WriteLine("OK: bitrate manual en GPU detectada (" + h264Encoder + ").");
            }
            Field<RadioButton>(form, "cpuMode").Checked = true;
            Field<ComboBox>(form, "compressionProfile").SelectedIndex = 2;
            Require(Field<ComboBox>(form, "codecChoice").SelectedIndex == 1 && Field<ComboBox>(form, "audioChoice").SelectedIndex == 2,
                "El perfil compacto no aplica HEVC y audio reducido.");
            Field<ComboBox>(form, "resolutionChoice").SelectedIndex = 3;
            Field<ComboBox>(form, "fpsChoice").SelectedIndex = 2;
            Require(Field<ComboBox>(form, "compressionProfile").SelectedIndex == 3, "Editar un ajuste debe marcar el perfil personalizado.");
            Field<ComboBox>(form, "audioChoice").SelectedIndex = 4;
            Field<RadioButton>(form, "durationMode").Checked = true;
            Require(!Field<ComboBox>(form, "codecChoice").Enabled && !Field<NumericUpDown>(form, "videoBitrate").Enabled,
                "La copia directa debe desactivar la compresión.");
            Field<CheckBox>(form, "compressOnce").Checked = true;
            Require(Field<ComboBox>(form, "codecChoice").Enabled && Field<RadioButton>(form, "cpuMode").Enabled,
                "La compresión previa debe permitir configurar el codificador.");
            Field<NumericUpDown>(form, "durationHours").Value = 0;
            Field<NumericUpDown>(form, "durationMinutes").Value = 0;
            Field<NumericUpDown>(form, "durationSeconds").Value = 15;
            string compact = Path.Combine(directory, "bucle-optimizado.mp4");
            SetDestination(form, compact);
            if (capture != null) SavePreview(form, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(capture)), "vista-compresion.png"));
            Export(form);
            CheckOutput(ffprobe, compact, 15, false);
            string details = VideoDetails(ffprobe, compact);
            Require(details.Contains("codec_name=hevc") && (details.Contains("width=854") || details.Contains("width=852")) && details.Contains("height=480") &&
                details.Contains("avg_frame_rate=24/1"), "No se aplicaron HEVC, 480p y 24 fps: " + details);
            Require(new FileInfo(compact).Length < new FileInfo(source).Length, "El bucle compacto es mayor que el original de prueba.");
            Require(Directory.GetFiles(directory, ".LooperMaster-*.mp4").Length == 0, "La compresión previa dejó temporales.");
            Console.WriteLine("OK: compresión única + bucle de 15 s, HEVC CPU, 480p, 24 fps y sin audio.");

            if (Field<bool>(form, "gpuHevcAvailable") || Field<bool>(form, "nvidiaHevcAvailable"))
            {
                Field<RadioButton>(form, "gpuMode").Checked = true;
                string gpuHevc = Path.Combine(directory, "bucle-hevc-gpu.mp4");
                SetDestination(form, gpuHevc);
                Export(form);
                CheckOutput(ffprobe, gpuHevc, 15, false);
                Require(VideoDetails(ffprobe, gpuHevc).Contains(hevcEncoder), "HEVC GPU no usa el motor detectado.");
                Console.WriteLine("OK: HEVC GPU con compresión previa.");
                Field<ComboBox>(form, "rateChoice").SelectedIndex = 3;
                Field<NumericUpDown>(form, "videoBitrate").Value = 400;
                Field<ComboBox>(form, "audioChoice").SelectedIndex = 2;
                string hevcBitrate = Path.Combine(directory, "hevc-bitrate-gpu.mp4");
                SetDestination(form, hevcBitrate);
                Export(form);
                CheckOutput(ffprobe, hevcBitrate, 15, true);
                string audio = System.Text.Encoding.UTF8.GetString(Run(ffprobe, new[] { "-v", "error", "-select_streams", "a:0",
                    "-show_entries", "stream=codec_name,bit_rate", "-of", "default=noprint_wrappers=1", hevcBitrate }));
                long rate = 0;
                foreach (string line in audio.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    if (line.StartsWith("bit_rate=", StringComparison.Ordinal)) Int64.TryParse(line.Substring(9), out rate);
                Require(audio.Contains("codec_name=aac") && rate > 60000 && rate < 130000, "No se aplicó el audio AAC reducido: " + audio);
                Console.WriteLine("OK: HEVC GPU con bitrate manual y audio AAC reducido.");
            }
            else Console.WriteLine("OMITIDO: HEVC GPU no superó la prueba; H.265 sigue disponible por CPU.");

            Field<RadioButton>(form, "cpuMode").Checked = true;
            Field<NumericUpDown>(form, "durationHours").Value = 100;
            SetDestination(form, Path.Combine(directory, "cancelado-preparacion.mp4"));
            Task cancelled = (Task)Call(form, "StartExportAsync");
            Call(form, "CancelExport");
            WaitUntil(delegate { return cancelled.IsCompleted; }, "cancelación de compresión previa");
            cancelled.GetAwaiter().GetResult();
            Require(!File.Exists(Field<string>(form, "outputPath")) && Directory.GetFiles(directory, ".LooperMaster-*.mp4").Length == 0,
                "La cancelación dejó archivos del clip comprimido.");
            Console.WriteLine("OK: cancelación durante la compresión previa y limpieza.");

            Field<ComboBox>(form, "compressionProfile").SelectedIndex = 0;
            Field<NumericUpDown>(form, "durationHours").Value = 0;
            Field<NumericUpDown>(form, "durationMinutes").Value = 10;
            string cancelledCopy = Path.Combine(directory, "cancelado-copia-optimizada.mp4");
            SetDestination(form, cancelledCopy);
            Task copying = (Task)Call(form, "StartExportAsync");
            WaitUntil(delegate { return Field<Label>(form, "status").Text.StartsWith("2/2", StringComparison.Ordinal) || copying.IsCompleted; }, "fase de copia optimizada");
            Require(!copying.IsCompleted, "La prueba terminó antes de poder cancelar la segunda fase.");
            Call(form, "CancelExport");
            WaitUntil(delegate { return copying.IsCompleted; }, "cancelación de la segunda fase");
            copying.GetAwaiter().GetResult();
            Require(!File.Exists(cancelledCopy) && Directory.GetFiles(directory, ".LooperMaster-*.mp4").Length == 0,
                "La segunda fase cancelada dejó archivos.");
            Console.WriteLine("OK: cancelación en la segunda fase y limpieza de ambos temporales.");

            Field<CheckBox>(form, "compressOnce").Checked = false;
            Field<RadioButton>(form, "repetitionsMode").Checked = true;
            Drop(form, Path.Combine(directory, "vídeo con sonido.mp4"));
            Field<ComboBox>(form, "compressionProfile").SelectedIndex = 1;
            string noUpscale = Path.Combine(directory, "sin-ampliar.mp4");
            SetDestination(form, noUpscale);
            Export(form);
            string smallDetails = VideoDetails(ffprobe, noUpscale);
            Require(smallDetails.Contains("width=320") && smallDetails.Contains("height=240") && smallDetails.Contains("avg_frame_rate=24/1"),
                "Los máximos aumentaron resolución o fps: " + smallDetails);
            Console.WriteLine("OK: los límites de resolución y fps no amplían un vídeo menor.");
            Field<ComboBox>(form, "compressionProfile").SelectedIndex = 0;
            Field<NumericUpDown>(form, "durationHours").Value = 0;
            Field<NumericUpDown>(form, "durationMinutes").Value = 0;
        }

        private static void TestAutomaticGpu(MainForm form, string ffmpeg, string ffprobe, string directory)
        {
            FieldInfo h264Flag = typeof(MainForm).GetField("nvidiaAvailable", PrivateInstance);
            FieldInfo hevcFlag = typeof(MainForm).GetField("nvidiaHevcAvailable", PrivateInstance);
            bool h264 = (bool)h264Flag.GetValue(form);
            bool hevc = (bool)hevcFlag.GetValue(form);
            FieldInfo amdH264Flag = typeof(MainForm).GetField("gpuAvailable", PrivateInstance);
            FieldInfo amdHevcFlag = typeof(MainForm).GetField("gpuHevcAvailable", PrivateInstance);
            bool amdH264 = (bool)amdH264Flag.GetValue(form);
            bool amdHevc = (bool)amdHevcFlag.GetValue(form);
            RadioButton gpu = Field<RadioButton>(form, "gpuMode");
            RadioButton cpu = Field<RadioButton>(form, "cpuMode");
            ComboBox codec = Field<ComboBox>(form, "codecChoice");
            CheckBox prepare = Field<CheckBox>(form, "compressOnce");
            try
            {
                Drop(form, Path.Combine(directory, "vídeo con sonido.mp4"));
                Field<RadioButton>(form, "repetitionsMode").Checked = true;
                Field<ComboBox>(form, "compressionProfile").SelectedIndex = 0;
                prepare.Checked = false;

                // Exercise each vendor and mixed hardware without requiring both GPUs.
                amdH264Flag.SetValue(form, true);
                amdHevcFlag.SetValue(form, true);
                h264Flag.SetValue(form, false);
                hevcFlag.SetValue(form, false);
                Call(form, "UpdateControls");
                gpu.Checked = true;
                Require(gpu.Enabled && !cpu.Checked && Field<Button>(form, "create").Enabled &&
                    Field<Label>(form, "gpuStatus").Text.Contains("AMD AMF") && Field<Label>(form, "outputHelp").Text.Contains("AMD AMF"),
                    "GPU debe detectar y seleccionar AMD automáticamente.");
                h264Flag.SetValue(form, true);
                Call(form, "RefreshSuggestion");
                Call(form, "UpdateControls");
                Require(gpu.Checked && Field<Label>(form, "outputHelp").Text.Contains("NVIDIA NVENC"),
                    "Cuando ambas GPU son compatibles, debe priorizarse NVIDIA automáticamente.");
                amdH264Flag.SetValue(form, false);
                Call(form, "RefreshSuggestion");
                Require(Field<Label>(form, "gpuStatus").Text.Contains("NVIDIA NVENC") &&
                    Field<string>(form, "outputPath").Contains("_gpu") && gpu.Text == "GPU",
                    "NVIDIA debe utilizar el mismo selector GPU, sin exponer un selector por fabricante.");
                codec.SelectedIndex = 1;
                Require(gpu.Checked && gpu.Enabled && Field<Button>(form, "create").Enabled &&
                    Field<Label>(form, "outputHelp").Text.Contains("AMD AMF"),
                    "Cambiar de códec debe usar otra GPU compatible automáticamente.");
                amdHevcFlag.SetValue(form, false);
                Call(form, "RefreshSuggestion");
                Call(form, "UpdateControls");
                Require(gpu.Checked && !gpu.Enabled && !Field<Button>(form, "create").Enabled && cpu.Enabled && !cpu.Checked,
                    "Sin una GPU compatible debe bloquearse la exportación sin cambiar a CPU.");
                string blockedOutput = Path.Combine(directory, "nvenc-no-disponible.mp4");
                SetDestination(form, blockedOutput);
                ((Task)Call(form, "StartExportAsync")).GetAwaiter().GetResult();
                Require(!Field<bool>(form, "encoding") && !File.Exists(blockedOutput), "Se exportó con un códec NVENC no disponible.");
                hevcFlag.SetValue(form, true);
                Call(form, "UpdateControls");
                Require(gpu.Enabled && Field<Button>(form, "create").Enabled, "GPU debe habilitar HEVC NVENC automáticamente cuando está disponible.");
                Field<RadioButton>(form, "durationMode").Checked = true;
                Require(!gpu.Enabled && !cpu.Enabled, "La copia directa no debe usar un codificador.");
                prepare.Checked = true;
                Require(gpu.Enabled && gpu.Checked, "La compresión previa debe conservar la elección GPU.");
                Console.WriteLine("OK: sólo CPU/GPU, detección AMD/NVIDIA, prioridad con ambas GPU, compatibilidad por códec y bloqueo sin GPU.");

                cpu.Checked = true;
                h264Flag.SetValue(form, h264);
                hevcFlag.SetValue(form, hevc);
                amdH264Flag.SetValue(form, amdH264);
                amdHevcFlag.SetValue(form, amdHevc);
                prepare.Checked = false;
                Field<RadioButton>(form, "repetitionsMode").Checked = true;
                Field<ComboBox>(form, "compressionProfile").SelectedIndex = 0;
                Call(form, "UpdateControls");
                if (!h264)
                {
                    // Force the real failing path to verify cleanup and no silent CPU fallback.
                    h264Flag.SetValue(form, true);
                    Call(form, "UpdateControls");
                    gpu.Checked = true;
                    string failedOutput = Path.Combine(directory, "nvenc-error.mp4");
                    SetDestination(form, failedOutput);
                    Task failed = (Task)Call(form, "StartExportAsync");
                    WaitUntil(delegate { return failed.IsCompleted; }, "error NVENC sin hardware");
                    failed.GetAwaiter().GetResult();
                    string message = Field<Label>(form, "status").Text;
                    Require(message.Contains("NVIDIA NVENC ha fallado") && message.Contains("seleccionar CPU") &&
                        gpu.Checked && !cpu.Checked && !File.Exists(failedOutput),
                        "Un fallo NVENC debe explicarse y no producir un resultado mediante otro motor: " + message);
                    Require(Directory.GetFiles(directory, ".LooperMaster-*.mp4").Length == 0, "Un fallo NVENC dejó temporales.");
                    h264Flag.SetValue(form, false);
                    cpu.Checked = true;
                    Console.WriteLine("OK: fallo real NVENC sin controlador, mensaje NVIDIA, sin cambio de motor y limpieza.");
                }

                for (int index = 0; index < 2; index++)
                {
                    bool isHevc = index == 1;
                    if (!(isHevc ? hevc : h264))
                    {
                        Console.WriteLine("OMITIDO: codificación real NVIDIA " + (isHevc ? "HEVC" : "H.264") + " no disponible en este equipo.");
                        continue;
                    }
                    Drop(form, Path.Combine(directory, "vídeo con sonido.mp4"));
                    prepare.Checked = false;
                    Field<RadioButton>(form, "repetitionsMode").Checked = true;
                    Field<ComboBox>(form, "compressionProfile").SelectedIndex = 0;
                    codec.SelectedIndex = index;
                    gpu.Checked = true;
                    Field<NumericUpDown>(form, "count").Value = 3;
                    string tag = isHevc ? "hevc_nvenc" : "h264_nvenc";
                    string repeated = Path.Combine(directory, tag + "-repetido.mp4");
                    SetDestination(form, repeated);
                    Export(form);
                    CheckOutput(ffprobe, repeated, 6, true);
                    Require(VideoDetails(ffprobe, repeated).Contains(tag), "La exportación no usa " + tag);
                    CheckRepeatedVideo(ffmpeg, repeated);
                    CheckRepeatedAudio(ffmpeg, repeated);

                    Drop(form, Path.Combine(directory, "compresión original.mp4"));
                    Field<NumericUpDown>(form, "count").Value = 1;
                    Field<ComboBox>(form, "rateChoice").SelectedIndex = 3;
                    Field<NumericUpDown>(form, "videoBitrate").Value = 3000;
                    string high = Path.Combine(directory, tag + "-3000.mp4");
                    SetDestination(form, high);
                    Export(form);
                    Field<NumericUpDown>(form, "videoBitrate").Value = 500;
                    string low = Path.Combine(directory, tag + "-500.mp4");
                    SetDestination(form, low);
                    Export(form);
                    CheckOutput(ffprobe, low, 4, true);
                    Require(new FileInfo(low).Length < new FileInfo(high).Length * .75, "El bitrate NVENC no reduce el tamaño.");

                    Field<RadioButton>(form, "durationMode").Checked = true;
                    prepare.Checked = true;
                    Field<NumericUpDown>(form, "durationHours").Value = 0;
                    Field<NumericUpDown>(form, "durationMinutes").Value = 0;
                    Field<NumericUpDown>(form, "durationSeconds").Value = 7;
                    Field<ComboBox>(form, "resolutionChoice").SelectedIndex = 3;
                    Field<ComboBox>(form, "fpsChoice").SelectedIndex = 2;
                    Field<ComboBox>(form, "audioChoice").SelectedIndex = 4;
                    string timed = Path.Combine(directory, tag + "-optimizado.mp4");
                    SetDestination(form, timed);
                    Export(form);
                    CheckOutput(ffprobe, timed, 7, false);
                    string details = VideoDetails(ffprobe, timed);
                    Require(details.Contains(tag) && details.Contains("height=480") && details.Contains("avg_frame_rate=24/1"),
                        "La compresión previa NVENC no aplica motor, resolución y fps: " + details);
                    Require(Directory.GetFiles(directory, ".LooperMaster-*.mp4").Length == 0, "NVENC dejó archivos temporales.");
                    Console.WriteLine("OK: " + tag + ", repeticiones con audio, bitrate 3000/500, compresión previa, 480p, 24 fps y sin audio.");
                }
            }
            finally
            {
                cpu.Checked = true;
                h264Flag.SetValue(form, h264);
                hevcFlag.SetValue(form, hevc);
                amdH264Flag.SetValue(form, amdH264);
                amdHevcFlag.SetValue(form, amdHevc);
                prepare.Checked = false;
                Field<RadioButton>(form, "repetitionsMode").Checked = true;
                Field<ComboBox>(form, "compressionProfile").SelectedIndex = 0;
                Call(form, "UpdateControls");
            }
        }

        private static void Export(MainForm form)
        {
            Task task = (Task)Call(form, "StartExportAsync");
            WaitUntil(delegate { return task.IsCompleted; }, "exportación");
            task.GetAwaiter().GetResult();
            string status = Field<Label>(form, "status").Text;
            Require(status == "Vídeo creado correctamente.", "La exportación no terminó correctamente. Estado: " + status +
                "; ffmpeg: " + Field<string>(form, "ffmpeg") + "; entrada: " + Field<string>(form, "inputPath") +
                "; destino: " + Field<string>(form, "outputPath"));
        }

        private static string CheckOutput(string ffprobe, string path, double expected, bool hasAudio)
        {
            string details = System.Text.Encoding.UTF8.GetString(Run(ffprobe, new[] { "-v", "error", "-show_entries",
                "format=duration:stream=codec_type,width,height", "-of", "default=noprint_wrappers=1", path }));
            Require(details.Contains("codec_type=video"), "El resultado no contiene vídeo.");
            Require(details.Contains("codec_type=audio") == hasAudio, "La pista de audio no coincide con el original.");
            double duration = -1;
            foreach (string line in details.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("duration=", StringComparison.Ordinal))
                    Double.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
            Require(Math.Abs(duration - expected) < 0.3, "Duración inesperada: " + duration.ToString(CultureInfo.InvariantCulture));
            return details;
        }

        private static string FirstVideoPacketHash(string ffprobe, string path)
        {
            return System.Text.Encoding.UTF8.GetString(Run(ffprobe, new[] { "-v", "error", "-select_streams", "v:0",
                "-read_intervals", "%+#1", "-show_entries", "packet=data_hash", "-show_data_hash", "md5",
                "-of", "default=noprint_wrappers=1:nokey=1", path })).Trim();
        }

        private static void CheckRepeatedVideo(string ffmpeg, string path)
        {
            byte[] frames = Run(ffmpeg, new[] { "-v", "error", "-i", path, "-vf",
                "select='eq(n,0)+eq(n,48)+eq(n,96)',scale=64:48", "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1" });
            int frameBytes = 64 * 48 * 3;
            Require(frames.Length == frameBytes * 3, "No se pudieron extraer tres fotogramas equivalentes.");
            for (int frame = 1; frame < 3; frame++)
            {
                long difference = 0;
                for (int i = 0; i < frameBytes; i++) difference += Math.Abs((int)frames[i] - frames[frame * frameBytes + i]);
                Require((double)difference / frameBytes < 15, "Los fotogramas de las repeticiones no coinciden.");
            }
        }

        private static void CheckRepeatedAudio(string ffmpeg, string path)
        {
            byte[] samples = Run(ffmpeg, new[] { "-v", "error", "-i", path, "-map", "0:a:0", "-ac", "1", "-ar", "8000", "-f", "s16le", "pipe:1" });
            for (int repetition = 0; repetition < 3; repetition++)
            {
                int start = (int)((0.5 + repetition * 2.0) * 8000) * 2;
                int length = 800 * 2;
                Require(samples.Length >= start + length, "Faltan muestras de audio en la repetición.");
                long energy = 0;
                for (int i = start; i < start + length; i += 2) energy += Math.Abs((int)BitConverter.ToInt16(samples, i));
                Require(energy / 800 > 100, "Una repetición no contiene el audio esperado.");
            }
        }

        private static byte[] Run(string executable, string[] args)
        {
            using (Process process = MediaTools.CreateProcess(executable, args))
            using (MemoryStream output = new MemoryStream())
            {
                process.Start();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                process.StandardOutput.BaseStream.CopyTo(output);
                process.WaitForExit();
                string error = errors.GetAwaiter().GetResult();
                Require(process.ExitCode == 0, Path.GetFileName(executable) + ": " + error);
                return output.ToArray();
            }
        }

        private static void RenderControls(Control parent, Graphics graphics, Point offset)
        {
            foreach (Control control in parent.Controls)
            {
                IntPtr handle = control.Handle;
                if (!control.Visible || control.Width <= 0 || control.Height <= 0) continue;
                System.Drawing.Drawing2D.GraphicsState state = graphics.Save();
                graphics.SetClip(new Rectangle(offset, parent.ClientSize), System.Drawing.Drawing2D.CombineMode.Intersect);
                Point location = new Point(offset.X + control.Left, offset.Y + control.Top);
                using (Bitmap image = new Bitmap(control.Width, control.Height))
                {
                    control.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                    graphics.DrawImageUnscaled(image, location);
                }
                RenderControls(control, graphics, location);
                graphics.Restore(state);
            }
        }

        private static void SavePreview(MainForm form, string path)
        {
            if (!form.Visible)
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-10000, -10000);
                form.Show();
            }
            Application.DoEvents();
            using (Bitmap image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(form.BackColor);
                RenderControls(form, graphics, Point.Empty);
                image.Save(path, ImageFormat.Png);
            }
        }

        private static T Field<T>(MainForm form, string name)
        {
            return (T)typeof(MainForm).GetField(name, PrivateInstance).GetValue(form);
        }

        private static object Call(MainForm form, string name, params object[] args)
        {
            return typeof(MainForm).GetMethod(name, PrivateInstance).Invoke(form, args);
        }

        private static void WaitUntil(Func<bool> condition, string operation)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition())
            {
                Application.DoEvents();
                Thread.Sleep(10);
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("Tiempo agotado durante " + operation + ".");
            }
            Application.DoEvents();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
