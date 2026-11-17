using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LooperMaster
{
    internal sealed partial class MainForm
    {
        private async Task PrepareFfmpegAsync()
        {
            if (preparingFfmpeg || ffmpegReady) return;
            preparingFfmpeg = true;
            CancellationTokenSource attempt = new CancellationTokenSource();
            setupCancellation = attempt;
            readyBadge.Text = "Comprobando FFmpeg…";
            readyBadge.ForeColor = Muted;
            progress.Style = ProgressBarStyle.Marquee;
            progressText.Text = "";
            status.Text = "Buscando FFmpeg en tu equipo…";
            tips.SetToolTip(status, "");
            UpdateControls();
            try
            {
                Progress<SetupProgress> updates = new Progress<SetupProgress>(delegate(SetupProgress update)
                {
                    if (IsDisposed || setupCancellation != attempt || attempt.IsCancellationRequested) return;
                    status.Text = update.Message;
                    progress.Style = update.Percentage.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                    progress.Value = update.Percentage ?? 0;
                    progressText.Text = update.Percentage.HasValue ? update.Percentage.Value + " %" : "";
                });
                FfmpegTools tools = await FfmpegSetup.EnsureAsync(updates, attempt.Token);
                ffmpeg = tools.Ffmpeg;
                ffprobe = tools.Ffprobe;
                ffmpegReady = true;
                readyBadge.Text = "●  FFmpeg listo";
                readyBadge.ForeColor = Accent;
                tips.SetToolTip(readyBadge, ffmpeg);
                status.Text = inputPath == null ? "FFmpeg listo. Añade un vídeo para empezar." : "Listo para crear tu vídeo.";
                gpuDetectionCompleted = false;
            }
            catch (OperationCanceledException)
            {
                readyBadge.Text = "FFmpeg pendiente";
                status.Text = "Preparación cancelada. Pulsa Reintentar FFmpeg para continuar.";
            }
            catch (Exception ex)
            {
                readyBadge.Text = "FFmpeg no disponible";
                status.Text = "No se pudo preparar FFmpeg. Pulsa Reintentar FFmpeg.";
                tips.SetToolTip(status, ex.Message);
                if (!closeWhenDone && showErrorDialogs)
                    MessageBox.Show(this, ex.Message + "\n\nComprueba tu conexión y pulsa Reintentar FFmpeg. También puedes colocar ffmpeg.exe junto a LooperMaster.",
                        "No se pudo preparar FFmpeg", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                preparingFfmpeg = false;
                attempt.Dispose();
                setupCancellation = null;
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = 0;
                progressText.Text = "";
                RefreshSuggestion();
                UpdateControls();
                if (closeWhenDone) Close();
            }
            if (ffmpegReady && !IsDisposed && !closeWhenDone) await DetectGpuAsync();
        }
    }
}
