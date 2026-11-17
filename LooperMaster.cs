using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("LooperMaster")]
[assembly: AssemblyDescription("Crea vídeos en bucle con FFmpeg")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace LooperMaster
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal static class MediaTools
    {
        internal static async Task<string> ReadErrorTailAsync(TextReader reader)
        {
            const int limit = 1500;
            char[] buffer = new char[4096];
            StringBuilder tail = new StringBuilder(limit);
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                int keep = Math.Min(read, limit);
                int discard = Math.Max(0, tail.Length + keep - limit);
                tail.Remove(0, discard);
                tail.Append(buffer, read - keep, keep);
            }
            return tail.ToString().Trim();
        }

        // ProcessStartInfo.Arguments uses the Windows command-line quoting rules.
        public static string Quote(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                if (c == '"')
                {
                    result.Append('\\', slashes * 2 + 1);
                    result.Append('"');
                }
                else
                {
                    result.Append('\\', slashes);
                    result.Append(c);
                }
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            result.Append('"');
            return result.ToString();
        }

        public static string FindExecutable(string name, string preferredDirectory)
        {
            return FindExecutables(name, ExecutableDirectories(preferredDirectory)).FirstOrDefault();
        }

        internal static IEnumerable<string> ExecutableDirectories(string preferredDirectory)
        {
            List<string> directories = new List<string>();
            if (!String.IsNullOrEmpty(preferredDirectory)) directories.Add(preferredDirectory);
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            directories.Add(appDirectory);
            directories.Add(Path.Combine(appDirectory, "bin"));
            foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Process,
                EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                string path = Environment.GetEnvironmentVariable("PATH", target) ?? "";
                directories.AddRange(path.Split(Path.PathSeparator));
            }
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Links"));
            directories.Add(FfmpegSetup.InstallDirectory);
            return directories;
        }

        internal static IEnumerable<string> FindExecutables(string name, IEnumerable<string> directories)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in directories)
            {
                string candidate = null;
                try
                {
                    if (String.IsNullOrWhiteSpace(directory)) continue;
                    candidate = Path.GetFullPath(Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')), name));
                }
                catch (Exception) { /* Ignore invalid PATH entries. */ }
                if (candidate != null && seen.Add(candidate) && File.Exists(candidate)) yield return candidate;
            }
        }

        public static Process CreateProcess(string executable, IEnumerable<string> arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = executable;
            info.Arguments = String.Join(" ", arguments.Select(Quote).ToArray());
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            return new Process { StartInfo = info };
        }

        public static string[] AmfEncodingArguments(bool hevc = false, int quality = 20, int bitrateKbps = 0)
        {
            List<string> args = new List<string> { "-c:v", hevc ? "hevc_amf" : "h264_amf", "-usage", "transcoding", "-quality", "balanced" };
            if (bitrateKbps > 0)
                args.AddRange(new[] { "-rc", "vbr_peak", "-b:v", bitrateKbps + "k", "-maxrate", (bitrateKbps * 3 / 2) + "k", "-bufsize", (bitrateKbps * 2) + "k" });
            else
            {
                args.AddRange(new[] { "-rc", "cqp", "-qp_i", quality.ToString(CultureInfo.InvariantCulture), "-qp_p", (quality + 2).ToString(CultureInfo.InvariantCulture) });
                if (!hevc) args.AddRange(new[] { "-qp_b", (quality + 4).ToString(CultureInfo.InvariantCulture) });
            }
            return args.ToArray();
        }

        public static string[] NvencEncodingArguments(bool hevc = false, int quality = 20, int bitrateKbps = 0)
        {
            List<string> args = new List<string> { "-c:v", hevc ? "hevc_nvenc" : "h264_nvenc",
                "-preset", "p4", "-tune", "hq", "-rc", "vbr" };
            if (bitrateKbps > 0)
                args.AddRange(new[] { "-b:v", bitrateKbps + "k", "-maxrate", (bitrateKbps * 3 / 2) + "k", "-bufsize", (bitrateKbps * 2) + "k" });
            else
                args.AddRange(new[] { "-cq", quality.ToString(CultureInfo.InvariantCulture), "-b:v", "0" });
            return args.ToArray();
        }

        public static async Task<bool> CanEncodeGpuAsync(string ffmpeg, EncodingEngine engine, bool hevc = false)
        {
            if (ffmpeg == null || engine == EncodingEngine.Cpu) return false;
            try
            {
                List<string> args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin",
                    "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=2", "-frames:v", "2" };
                args.AddRange(engine == EncodingEngine.Nvidia ? NvencEncodingArguments(hevc) : AmfEncodingArguments(hevc));
                args.AddRange(new[] { "-f", "null", "-" });
                using (Process process = CreateProcess(ffmpeg, args))
                {
                    process.Start();
                    Task output = Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
                    if (await Task.WhenAny(output, Task.Delay(8000)) != output)
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        await Task.WhenAny(output, Task.Delay(1000));
                        return false;
                    }
                    await output;
                    await Task.Run((Action)process.WaitForExit);
                    return process.ExitCode == 0;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (IOException) { return false; }
        }

        public static async Task<ProbeResult> ProbeAsync(string ffprobe, string input)
        {
            if (ffprobe == null) return new ProbeResult { Duration = null, HasVideo = true };

            using (Process process = CreateProcess(ffprobe, new[] {
                "-v", "error", "-select_streams", "v:0", "-show_entries",
                "stream=codec_type:format=duration", "-of", "default=noprint_wrappers=1", input
            }))
            {
                process.Start();
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                Task allOutput = Task.WhenAll(outputTask, errorTask);
                if (await Task.WhenAny(allOutput, Task.Delay(10000)) != allOutput)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new InvalidOperationException("No se pudo analizar el vídeo a tiempo.");
                }
                await allOutput;
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("FFprobe no pudo leer este archivo. Comprueba que sea un vídeo válido.");

                bool hasVideo = false;
                double duration;
                double? seconds = null;
                foreach (string line in outputTask.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Trim() == "codec_type=video") hasVideo = true;
                    if (line.StartsWith("duration=", StringComparison.Ordinal) &&
                        Double.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out duration) &&
                        duration > 0 && !Double.IsInfinity(duration) && !Double.IsNaN(duration)) seconds = duration;
                }
                return new ProbeResult { Duration = seconds, HasVideo = hasVideo };
            }
        }
    }

    internal enum EncodingEngine { Cpu, Amd, Nvidia }

    internal sealed class CompressionSettings
    {
        public bool Hevc;
        public int RateMode;
        public int BitrateKbps;
        public int Resolution;
        public int FrameRate;
        public int Audio;
        public int AudioKbps { get { return new[] { 192, 128, 96, 64, 0 }[Audio]; } }
        public string CodecName { get { return Hevc ? "H.265 / HEVC" : "H.264"; } }

        public IEnumerable<string> EncodeArguments(EncodingEngine engine)
        {
            List<string> filters = new List<string>();
            if (Resolution > 0)
            {
                int width = new[] { 0, 1920, 1280, 854 }[Resolution];
                int height = new[] { 0, 1080, 720, 480 }[Resolution];
                filters.Add("scale=w='min(iw," + width + ")':h='min(ih," + height + ")':force_original_aspect_ratio=decrease:force_divisible_by=2");
            }
            filters.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");
            if (FrameRate > 0)
            {
                string fps = FrameRate == 1 ? "30" : "24";
                filters.Add("fps=fps='if(gt(source_fps,0),min(source_fps," + fps + ")," + fps + ")'");
            }
            List<string> args = new List<string> { "-vf", String.Join(",", filters.ToArray()) };
            int quality = new[] { 18, 23, 28, 23 }[RateMode];
            if (engine == EncodingEngine.Amd) args.AddRange(MediaTools.AmfEncodingArguments(Hevc, quality + 2, RateMode == 3 ? BitrateKbps : 0));
            else if (engine == EncodingEngine.Nvidia) args.AddRange(MediaTools.NvencEncodingArguments(Hevc, quality + 2, RateMode == 3 ? BitrateKbps : 0));
            else
            {
                args.AddRange(new[] { "-c:v", Hevc ? "libx265" : "libx264", "-preset", "medium" });
                if (RateMode == 3)
                    args.AddRange(new[] { "-b:v", BitrateKbps + "k", "-maxrate", (BitrateKbps * 3 / 2) + "k", "-bufsize", (BitrateKbps * 2) + "k" });
                else args.AddRange(new[] { "-crf", quality.ToString(CultureInfo.InvariantCulture) });
            }
            args.AddRange(new[] { "-pix_fmt", "yuv420p" });
            if (Hevc) args.AddRange(new[] { "-tag:v", "hvc1" });
            if (AudioKbps == 0) args.Add("-an");
            else args.AddRange(new[] { "-c:a", "aac", "-b:a", AudioKbps + "k" });
            return args;
        }
    }

    internal sealed class ProbeResult
    {
        public double? Duration;
        public bool HasVideo;
    }

    internal sealed partial class MainForm : Form
    {
        private static readonly Color Background = UiColors.Background;
        private static readonly Color Surface = UiColors.Surface;
        private static readonly Color Foreground = UiColors.Text;
        private static readonly Color Muted = UiColors.Muted;
        private static readonly Color Accent = UiColors.Accent;

        private string ffmpeg;
        private string ffprobe;
        private readonly bool showErrorDialogs;
        private readonly DropZone dropZone;
        private readonly Label dropTitle;
        private readonly Label dropDetails;
        private readonly Button chooseVideo;
        private readonly RadioButton repetitionsMode;
        private readonly RadioButton durationMode;
        private readonly RadioButton cpuMode;
        private readonly RadioButton gpuMode;
        private readonly Label gpuStatus;
        private readonly Label gpuHelp;
        private readonly CheckBox compressOnce;
        private readonly ComboBox compressionProfile;
        private readonly ComboBox codecChoice;
        private readonly ComboBox resolutionChoice;
        private readonly ComboBox fpsChoice;
        private readonly ComboBox rateChoice;
        private readonly ComboBox audioChoice;
        private readonly NumericUpDown videoBitrate;
        private readonly Label compressionHint;
        private readonly NumericUpDown count;
        private readonly NumericUpDown durationHours;
        private readonly NumericUpDown durationMinutes;
        private readonly NumericUpDown durationSeconds;
        private readonly Label estimate;
        private readonly TextBox destination;
        private readonly Button chooseDestination;
        private readonly Label outputHelp;
        private readonly Label status;
        private readonly ProgressTrack progress;
        private readonly Label progressText;
        private readonly Button create;
        private readonly Button cancel;
        private readonly Button showResult;

        private string inputPath;
        private string outputPath;
        private double? inputDuration;
        private bool destinationCustomized;
        private bool probing;
        private bool encoding;
        private bool cancelRequested;
        private bool closeWhenDone;
        private int selectionVersion;
        private Process encoder;
        private bool gpuAvailable;
        private bool detectingGpu;
        private bool gpuDetectionCompleted;
        private bool gpuHevcAvailable;
        private bool nvidiaAvailable;
        private bool nvidiaHevcAvailable;
        private bool applyingProfile;
        private bool preparingFfmpeg;
        private bool ffmpegReady;
        private CancellationTokenSource setupCancellation;

        private static Label MakeLabel(string text, int x, int y, int width, int height, float size, FontStyle style, Color color)
        {
            return new Label { Text = text, Left = x, Top = y, Width = width, Height = height,
                Font = new Font("Segoe UI", size, style), ForeColor = color, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft };
        }

        private static RadioButton MakeMode(string text, int x, int y, int width, bool selected)
        {
            return new ChoiceButton { Text = text, Left = x, Top = y, Width = width, Height = 42,
                Checked = selected, ForeColor = Foreground, BackColor = Background,
                Font = new Font("Segoe UI", 9.5F), UseVisualStyleBackColor = false };
        }

        private static NumericUpDown MakeNumber(int x, int y, int width, int minimum, int maximum, int value, string accessibleName)
        {
            return new OptionNumber { Left = x, Top = y, Width = width, Height = 28,
                Minimum = minimum, Maximum = maximum, Value = value, Font = new Font("Segoe UI", 10F),
                BackColor = UiColors.Input, ForeColor = Foreground, BorderStyle = BorderStyle.None,
                AccessibleName = accessibleName };
        }

        private static ComboBox MakeCombo(int x, int y, int width, string name, string[] choices)
        {
            ComboBox combo = new OptionCombo { Left = x, Top = y, Width = width, DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Foreground,
                Font = new Font("Segoe UI", 9F), AccessibleName = name };
            combo.Items.AddRange(choices);
            combo.SelectedIndex = 0;
            return combo;
        }

        private CompressionSettings ReadCompression()
        {
            return new CompressionSettings { Hevc = codecChoice.SelectedIndex == 1, RateMode = rateChoice.SelectedIndex,
                BitrateKbps = Decimal.ToInt32(videoBitrate.Value), Resolution = resolutionChoice.SelectedIndex,
                FrameRate = fpsChoice.SelectedIndex, Audio = audioChoice.SelectedIndex };
        }

        private bool NeedsEncoding { get { return repetitionsMode.Checked || compressOnce.Checked; } }
        private bool SelectedAmdAvailable { get { return codecChoice.SelectedIndex == 1 ? gpuHevcAvailable : gpuAvailable; } }
        private bool SelectedNvidiaAvailable { get { return codecChoice.SelectedIndex == 1 ? nvidiaHevcAvailable : nvidiaAvailable; } }
        private EncodingEngine? DetectedGpuEngine
        {
            get
            {
                // Prefer NVIDIA when both vendors can encode the selected codec.
                if (SelectedNvidiaAvailable) return EncodingEngine.Nvidia;
                if (SelectedAmdAvailable) return EncodingEngine.Amd;
                return null;
            }
        }
        private string DetectedGpuName { get { return DetectedGpuEngine == EncodingEngine.Nvidia ? "NVIDIA NVENC" : DetectedGpuEngine == EncodingEngine.Amd ? "AMD AMF" : "GPU"; } }
        private string SelectedEngineName { get { return gpuMode.Checked ? DetectedGpuName : "CPU"; } }
        private bool SelectedEncoderAvailable { get { return !gpuMode.Checked || DetectedGpuEngine.HasValue; } }

        private void ProfileChanged(object sender, EventArgs e)
        {
            if (applyingProfile) return;
            int profile = compressionProfile.SelectedIndex;
            if (profile < 3)
            {
                applyingProfile = true;
                codecChoice.SelectedIndex = profile == 2 ? 1 : 0;
                resolutionChoice.SelectedIndex = profile;
                fpsChoice.SelectedIndex = profile == 0 ? 0 : 1;
                rateChoice.SelectedIndex = profile;
                audioChoice.SelectedIndex = profile;
                applyingProfile = false;
            }
            ModeOrDurationChanged(sender, e);
        }

        private void CompressionOptionChanged(object sender, EventArgs e)
        {
            if (applyingProfile) return;
            applyingProfile = true;
            compressionProfile.SelectedIndex = 3;
            applyingProfile = false;
            ModeOrDurationChanged(sender, e);
        }

        private static Button MakeButton(string text, int x, int y, int width, int height, bool primary)
        {
            Button button = new ActionButton { Primary = primary, Text = text, Left = x, Top = y, Width = width, Height = height,
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface,
                ForeColor = primary ? Background : Foreground, Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(70, 87, 98);
            return button;
        }

        private void UpdateControls()
        {
            bool ready = !encoding && !probing && !preparingFfmpeg;
            chooseVideo.Enabled = !encoding && !preparingFfmpeg;
            repetitionsMode.Enabled = ready;
            durationMode.Enabled = ready;
            count.Enabled = ready && repetitionsMode.Checked;
            repetitionsFields.Visible = repetitionsMode.Checked;
            durationFields.Visible = durationMode.Checked;
            compressOnce.Visible = durationMode.Checked;
            loopHint.Visible = repetitionsMode.Checked;
            durationHours.Enabled = ready && durationMode.Checked;
            durationMinutes.Enabled = ready && durationMode.Checked;
            durationSeconds.Enabled = ready && durationMode.Checked;
            compressOnce.Enabled = ready && durationMode.Checked;
            bool recode = NeedsEncoding;
            cpuMode.Enabled = ready && recode;
            gpuMode.Enabled = ready && recode && DetectedGpuEngine.HasValue;
            foreach (ComboBox option in new[] { compressionProfile, codecChoice, resolutionChoice, fpsChoice, rateChoice, audioChoice })
                option.Enabled = ready && recode;
            videoBitrate.Enabled = ready && recode && rateChoice.SelectedIndex == 3;
            chooseDestination.Enabled = ready && inputPath != null;
            create.Text = !ffmpegReady && !preparingFfmpeg ? "Reintentar FFmpeg" : "Crear vídeo";
            create.Enabled = !ffmpegReady ? !preparingFfmpeg : ready && inputPath != null && outputPath != null && ffmpeg != null &&
                (!durationMode.Checked || GetTargetSeconds() > 0) && (!recode || SelectedEncoderAvailable);
            cancel.Enabled = preparingFfmpeg ? setupCancellation != null && !setupCancellation.IsCancellationRequested : encoding && !cancelRequested;
            showResult.Enabled = ready && lastOutput != null && File.Exists(lastOutput);
        }

        private async Task DetectGpuAsync()
        {
            if (!ffmpegReady || detectingGpu || gpuDetectionCompleted) return;
            detectingGpu = true;
            RefreshSuggestion();
            bool[] available = await Task.WhenAll(
                MediaTools.CanEncodeGpuAsync(ffmpeg, EncodingEngine.Amd),
                MediaTools.CanEncodeGpuAsync(ffmpeg, EncodingEngine.Amd, true),
                MediaTools.CanEncodeGpuAsync(ffmpeg, EncodingEngine.Nvidia),
                MediaTools.CanEncodeGpuAsync(ffmpeg, EncodingEngine.Nvidia, true));
            if (IsDisposed) return;
            gpuAvailable = available[0];
            gpuHevcAvailable = available[1];
            nvidiaAvailable = available[2];
            nvidiaHevcAvailable = available[3];
            detectingGpu = false;
            gpuDetectionCompleted = true;
            RefreshSuggestion();
            UpdateControls();
        }

        private void ModeOrDurationChanged(object sender, EventArgs e)
        {
            RefreshSuggestion();
            lastOutput = null;
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 0;
            progressText.Text = "";
            if (ffmpegReady && !preparingFfmpeg)
                status.Text = inputPath == null ? "Añade un vídeo para empezar." : "Listo para crear tu vídeo.";
            UpdateControls();
        }

        private int GetTargetSeconds()
        {
            return Decimal.ToInt32(durationHours.Value) * 3600 +
                Decimal.ToInt32(durationMinutes.Value) * 60 + Decimal.ToInt32(durationSeconds.Value);
        }

        private string lastOutput;

        private void DropEntered(object sender, DragEventArgs e)
        {
            if (!encoding && !preparingFfmpeg && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length == 1 && File.Exists(files[0]))
                {
                    e.Effect = DragDropEffects.Copy;
                    dropZone.Highlighted = true;
                    dropZone.Invalidate();
                    return;
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void DropLeft(object sender, EventArgs e)
        {
            dropZone.Highlighted = false;
            dropZone.Invalidate();
        }

        private void VideoDropped(object sender, DragEventArgs e)
        {
            DropLeft(sender, EventArgs.Empty);
            if (encoding || preparingFfmpeg) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length == 1) SelectVideo(files[0]);
        }

        private void BrowseVideo()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona un vídeo";
                dialog.Filter = "Vídeos|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.mpeg;*.mpg;*.wmv;*.flv;*.gif;*.ts;*.m2ts|Todos los archivos|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) SelectVideo(dialog.FileName);
            }
        }

        private async void SelectVideo(string path)
        {
            if (preparingFfmpeg) return;
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "No se encuentra el archivo seleccionado.", "LooperMaster", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int version = ++selectionVersion;
            inputPath = Path.GetFullPath(path);
            dropZone.HasFile = true;
            dropZone.Invalidate();
            inputDuration = null;
            destinationCustomized = false;
            lastOutput = null;
            probing = true;
            dropTitle.Text = Path.GetFileName(inputPath);
            dropDetails.Text = "Analizando vídeo…";
            chooseVideo.Text = "Cambiar vídeo";
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 0;
            progressText.Text = "";
            RefreshSuggestion();
            UpdateControls();

            try
            {
                ProbeResult info = await MediaTools.ProbeAsync(ffprobe, inputPath);
                if (version != selectionVersion || IsDisposed) return;
                if (!info.HasVideo) throw new InvalidOperationException("El archivo no contiene una pista de vídeo.");
                inputDuration = info.Duration;
                dropDetails.Text = info.Duration.HasValue ? "Duración original: " + FormatDuration(info.Duration.Value) : "Vídeo seleccionado";
                if (ffmpegReady) status.Text = "Listo para crear tu vídeo.";
            }
            catch (Exception ex)
            {
                if (version != selectionVersion || IsDisposed) return;
                inputPath = null;
                dropZone.HasFile = false;
                dropZone.Invalidate();
                outputPath = null;
                destination.Text = "";
                dropTitle.Text = "Arrastra un vídeo aquí";
                dropDetails.Text = "O selecciónalo desde tu equipo";
                chooseVideo.Text = "Elegir vídeo";
                status.Text = "Selecciona un vídeo válido para continuar.";
                MessageBox.Show(this, ex.Message, "No se pudo abrir el vídeo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (version == selectionVersion && !IsDisposed)
                {
                    probing = false;
                    RefreshSuggestion();
                    UpdateControls();
                }
            }
        }

        private void RefreshSuggestion()
        {
            bool timed = durationMode.Checked;
            bool recode = NeedsEncoding;
            CompressionSettings settings = ReadCompression();
            gpuStatus.Text = !gpuDetectionCompleted ? (ffmpeg == null ? "GPU sin comprobar" : "Detectando GPU…") :
                DetectedGpuEngine.HasValue ? "●  GPU detectada: " + DetectedGpuName : "GPU no disponible para " + settings.CodecName;
            gpuStatus.ForeColor = gpuDetectionCompleted && DetectedGpuEngine.HasValue ? Accent : Muted;
            gpuHelp.Text = !recode ? "Copia directa, sin recodificar.\nConserva la calidad del original." :
                SelectedEngineName + " · " + settings.CodecName + "." +
                (timed ? "\nComprime el clip y repítelo por copia." : "\nComprime todas las reproducciones.");
            int targetSeconds = GetTargetSeconds();
            double? length = timed ? (double?)targetSeconds :
                inputDuration.HasValue ? inputDuration.Value * (double)count.Value : (double?)null;
            summaryDuration.Text = length.HasValue ? FormatDuration(length.Value) : "—";
            if (timed && targetSeconds == 0) estimate.Text = "Introduce una duración mayor que cero.";
            else if (!recode) estimate.Text = "Tamaño estimado: " + EstimateOutputSize(targetSeconds);
            else if (settings.RateMode == 3 && length.HasValue)
                estimate.Text = "Tamaño orientativo: " + FormatSize(length.Value * (settings.BitrateKbps + settings.AudioKbps) * 1000 / 8);
            else estimate.Text = length.HasValue ? "Tamaño variable según el contenido." : "Tamaño estimado: —";

            outputHelp.Text = !recode ? "MP4  ·  Copia rápida sin recodificar  ·  Duración aproximada" :
                "MP4  ·  " + settings.CodecName + " con " + SelectedEngineName +
                (settings.AudioKbps == 0 ? "  ·  Sin audio" : "  ·  AAC " + settings.AudioKbps + " kbps");
            compressionHint.Text = !recode ? "Copia directa: conserva bitrate, resolución y audio. Activa «Comprimir el clip una vez» para reducir el tamaño." :
                settings.RateMode == 3 ? "Bitrate objetivo del vídeo, con margen para variaciones. El tamaño estimado incluye audio; no es un límite de archivo." :
                "Calidad según contenido: el bitrate varía para ahorrar espacio. La resolución y los fps sólo se reducen. HEVC requiere un reproductor compatible.";
            if (inputPath != null && !destinationCustomized)
            {
                string suffix = timed
                    ? "_bucle_" + durationHours.Value.ToString(CultureInfo.InvariantCulture) + "h" +
                        durationMinutes.Value.ToString("00", CultureInfo.InvariantCulture) + "m" +
                        durationSeconds.Value.ToString("00", CultureInfo.InvariantCulture) + "s" + (recode ? "_optimizado" : "")
                    : "_bucle_x" + count.Value.ToString(CultureInfo.InvariantCulture) + (gpuMode.Checked ? "_gpu" : "");
                if (recode && settings.Hevc) suffix += "_hevc";
                outputPath = Path.Combine(Path.GetDirectoryName(inputPath), Path.GetFileNameWithoutExtension(inputPath) + suffix + ".mp4");
                destination.Text = outputPath;
            }
        }

        private static string FormatSize(double size)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return "~" + size.ToString("0.#", CultureInfo.CurrentCulture) + " " + units[unit];
        }

        private string EstimateOutputSize(int targetSeconds)
        {
            if (inputPath == null || !inputDuration.HasValue || inputDuration.Value <= 0) return "—";
            try
            {
                double size = new FileInfo(inputPath).Length * (double)targetSeconds / inputDuration.Value;
                return FormatSize(size);
            }
            catch (IOException) { return "—"; }
            catch (UnauthorizedAccessException) { return "—"; }
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds) return "muy larga";
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            long hours = (long)span.TotalHours;
            return hours > 0
                ? String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, span.Minutes, span.Seconds)
                : String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", span.Minutes, span.Seconds);
        }

        private void BrowseDestination()
        {
            if (inputPath == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Guardar el vídeo en bucle";
                dialog.Filter = "Vídeo MP4|*.mp4";
                dialog.DefaultExt = "mp4";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = false;
                dialog.FileName = outputPath;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    outputPath = Path.GetFullPath(dialog.FileName);
                    destination.Text = outputPath;
                    destinationCustomized = true;
                    UpdateControls();
                }
            }
        }

        private List<string> InputArguments(string source, int loopCount)
        {
            return new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-nostats", "-progress", "pipe:1",
                "-stream_loop", loopCount.ToString(CultureInfo.InvariantCulture), "-i", source,
                "-map", "0:v:0", "-map", "0:a:0?" };
        }

        private async Task RunFfmpegAsync(List<string> args, double? expected, int progressStart, int progressSpan)
        {
            if (cancelRequested) throw new OperationCanceledException();
            progress.Style = expected.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            progress.Value = progressStart;
            progressText.Text = expected.HasValue ? progressStart + " %" : "";
            using (Process process = MediaTools.CreateProcess(ffmpeg, args))
            {
                encoder = process;
                try
                {
                    process.Start();
                    Task<string> errors = MediaTools.ReadErrorTailAsync(process.StandardError);
                    string line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        long microseconds;
                        if (expected.HasValue && expected.Value > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                            Int64.TryParse(line.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out microseconds))
                        {
                            double fraction = Math.Max(0, Math.Min(1, microseconds / 1000000.0 / expected.Value));
                            int percentage = Math.Min(99, progressStart + (int)(fraction * progressSpan));
                            if (progress.Value == percentage) continue;
                            progress.Value = percentage;
                            progressText.Text = percentage.ToString(CultureInfo.InvariantCulture) + " %";
                        }
                    }
                    await Task.Run((Action)process.WaitForExit);
                    string detail = await errors;
                    if (cancelRequested) throw new OperationCanceledException();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("FFmpeg no pudo crear el vídeo." + (detail.Length > 0 ? "\n\n" + detail : ""));
                    }
                }
                finally
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    encoder = null;
                }
            }
        }

        private async Task StartExportAsync()
        {
            if (encoding || probing || preparingFfmpeg || !ffmpegReady || inputPath == null || outputPath == null || ffmpeg == null) return;
            bool timed = durationMode.Checked;
            bool prepare = timed && compressOnce.Checked;
            bool recode = !timed || prepare;
            if (recode && !SelectedEncoderAvailable) return;
            EncodingEngine engine = recode && gpuMode.Checked ? DetectedGpuEngine.Value : EncodingEngine.Cpu;
            string engineName = SelectedEngineName;
            CompressionSettings settings = ReadCompression();
            int targetSeconds = GetTargetSeconds();
            if (timed && targetSeconds <= 0) return;
            if (!File.Exists(inputPath))
            {
                MessageBox.Show(this, "El vídeo original ya no está en la ubicación seleccionada.", "Archivo no encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (String.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Elige otro nombre o carpeta para proteger el vídeo original.", "Destino no válido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool replaceExisting = File.Exists(outputPath);
            if (replaceExisting && MessageBox.Show(this, "Ya existe un archivo con este nombre. ¿Quieres sustituirlo cuando termine la conversión?",
                "El archivo ya existe", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string temporary = Path.Combine(Path.GetDirectoryName(outputPath), ".LooperMaster-" + Guid.NewGuid().ToString("N") + ".mp4");
            string preparedClip = Path.Combine(Path.GetDirectoryName(outputPath), ".LooperMaster-" + Guid.NewGuid().ToString("N") + "-clip.mp4");
            int total = Decimal.ToInt32(count.Value);
            double? expected = timed ? (double?)targetSeconds : (inputDuration.HasValue ? inputDuration.Value * total : (double?)null);
            string finishedOutput = outputPath;
            bool preserveTemporary = false;
            encoding = true;
            cancelRequested = false;
            lastOutput = null;
            UpdateControls();
            try
            {
                string source = inputPath;
                if (prepare)
                {
                    status.Text = "1/2 · Comprimiendo el clip con " + engineName + "…";
                    List<string> first = InputArguments(inputPath, 0);
                    first.AddRange(settings.EncodeArguments(engine));
                    first.AddRange(new[] { "-movflags", "+faststart", "-n", preparedClip });
                    await RunFfmpegAsync(first, inputDuration, 0, 45);
                    source = preparedClip;
                }
                status.Text = prepare ? "2/2 · Repitiendo el clip optimizado…" : timed ? "Copiando el bucle…" :
                    "Codificando con " + engineName + "…";
                List<string> args = InputArguments(source, timed ? -1 : total - 1);
                if (timed) args.AddRange(new[] { "-t", targetSeconds.ToString(CultureInfo.InvariantCulture), "-c", "copy" });
                else args.AddRange(settings.EncodeArguments(engine));
                args.AddRange(new[] { "-movflags", "+faststart", "-n", temporary });
                await RunFfmpegAsync(args, expected, prepare ? 45 : 0, prepare ? 54 : 99);
                if (cancelRequested) throw new OperationCanceledException();
                try
                {
                    if (replaceExisting) File.Replace(temporary, finishedOutput, null);
                    else File.Move(temporary, finishedOutput);
                }
                catch (Exception ex)
                {
                    preserveTemporary = true;
                    throw new IOException("El vídeo se creó, pero no se pudo guardar con el nombre elegido. Se ha conservado aquí:\n" + temporary + "\n\n" + ex.Message);
                }
                lastOutput = finishedOutput;
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = 100;
                progressText.Text = "100 %";
                status.Text = "Vídeo creado correctamente.";
            }
            catch (OperationCanceledException)
            {
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = 0;
                progressText.Text = "";
                status.Text = "Conversión cancelada.";
            }
            catch (Exception ex)
            {
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = 0;
                progressText.Text = "";
                string guidance = !recode ? " Si el formato no es compatible con MP4, activa Comprimir el clip una vez." :
                    engine != EncodingEngine.Cpu ? " La codificación con " + engineName + " ha fallado. Puedes seleccionar CPU y volver a intentarlo." : "";
                status.Text = "No se pudo crear el vídeo: " + ex.Message.Replace('\r', ' ').Replace('\n', ' ') + guidance;
                if (!closeWhenDone && showErrorDialogs) MessageBox.Show(this, ex.Message + guidance, "Error al crear el vídeo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                foreach (string cleanup in new[] { preparedClip, preserveTemporary ? null : temporary })
                {
                    try { if (cleanup != null && File.Exists(cleanup)) File.Delete(cleanup); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                encoding = false;
                UpdateControls();
                if (closeWhenDone) Close();
            }
        }

        private void CancelExport()
        {
            if (preparingFfmpeg)
            {
                setupCancellation.Cancel();
                status.Text = "Cancelando preparación de FFmpeg…";
                UpdateControls();
                return;
            }
            if (!encoding || cancelRequested) return;
            cancelRequested = true;
            status.Text = "Cancelando…";
            UpdateControls();
            try { if (encoder != null && !encoder.HasExited) encoder.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private void HandleClosing(object sender, FormClosingEventArgs e)
        {
            if (preparingFfmpeg)
            {
                e.Cancel = true;
                closeWhenDone = true;
                CancelExport();
                return;
            }
            if (!encoding) return;
            e.Cancel = true;
            closeWhenDone = true;
            CancelExport();
        }

        private void ShowOutput()
        {
            if (lastOutput == null || !File.Exists(lastOutput)) return;
            try { Process.Start("explorer.exe", "/select," + MediaTools.Quote(lastOutput)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo abrir la carpeta", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
