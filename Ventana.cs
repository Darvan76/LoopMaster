using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LooperMaster
{
    internal sealed partial class MainForm
    {
        private readonly Panel header;
        private readonly Panel footer;
        private readonly Panel scrollHost;
        private readonly Panel workspace;
        private readonly SurfacePanel loopPanel;
        private readonly SurfacePanel compressionPanel;
        private readonly SurfacePanel exportPanel;
        private readonly Panel loopModes;
        private readonly Panel engineModes;
        private readonly Panel repetitionsFields;
        private readonly Panel durationFields;
        private readonly Control brandMark;
        private readonly Control fileMark;
        private readonly Label headerTitle;
        private readonly Label headerDescription;
        private readonly Label readyBadge;
        private readonly Label loopTitle;
        private readonly Label loopHint;
        private readonly Label compressionTitle;
        private readonly Label exportTitle;
        private readonly Label durationCaption;
        private readonly Label summaryDuration;
        private readonly Label outputTitle;
        private readonly Label outputFileName;
        private readonly Panel destinationFrame;
        private readonly Panel engineDivider;
        private readonly Panel outputDivider;
        private readonly List<Panel> optionFields = new List<Panel>();
        private readonly ToolTip tips;
        private readonly Icon productIcon;
        private bool layoutReady;
        private bool layingOut;
        private float uiScale = 1F;

        public MainForm() : this(true) { }

        internal MainForm(bool showErrorDialogs)
        {
            this.showErrorDialogs = showErrorDialogs;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "LooperMaster";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1060, 774);
            BackColor = Background;
            ForeColor = Foreground;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 450, ReshowDelay = 150 };
            productIcon = CreateProductIcon();
            Icon = productIcon;

            header = new Panel { BackColor = Background };
            footer = new Panel { BackColor = Background };
            scrollHost = new Panel { BackColor = Background, AutoScroll = true, TabStop = false };
            workspace = new Panel { BackColor = Background };
            scrollHost.Controls.Add(workspace);
            Controls.AddRange(new Control[] { header, scrollHost, footer });
            header.TabIndex = 0; scrollHost.TabIndex = 1; footer.TabIndex = 2;

            brandMark = new LoopMark();
            headerTitle = MakeLabel("LooperMaster", 0, 0, 0, 0, 17F, FontStyle.Bold, Foreground);
            headerDescription = MakeLabel("Tu vídeo. El tiempo que quieras.", 0, 0, 0, 0, 9F, FontStyle.Regular, Muted);
            readyBadge = MakeLabel("Comprobando FFmpeg…", 0, 0, 0, 0, 8.5F, FontStyle.Regular, Muted);
            readyBadge.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.AddRange(new Control[] { brandMark, headerTitle, headerDescription, readyBadge });

            dropZone = new DropZone { AllowDrop = true, TabIndex = 0 };
            fileMark = new VideoMark();
            dropTitle = MakeLabel("Arrastra tu vídeo aquí", 0, 0, 0, 0, 11F, FontStyle.Bold, Foreground);
            dropTitle.AutoEllipsis = true;
            dropDetails = MakeLabel("O elige un archivo de tu equipo", 0, 0, 0, 0, 9F, FontStyle.Regular, Muted);
            dropDetails.AutoEllipsis = true;
            chooseVideo = MakeButton("Elegir vídeo", 0, 0, 140, 36, false);
            chooseVideo.TabIndex = 0;
            dropZone.Controls.AddRange(new Control[] { fileMark, dropTitle, dropDetails, chooseVideo });
            chooseVideo.Click += delegate { BrowseVideo(); };
            foreach (Control target in new Control[] { dropZone, fileMark, dropTitle, dropDetails, chooseVideo })
            {
                target.AllowDrop = true;
                target.DragEnter += DropEntered;
                target.DragLeave += DropLeft;
                target.DragDrop += VideoDropped;
            }
            workspace.Controls.Add(dropZone);

            loopPanel = new SurfacePanel { TabIndex = 1 };
            loopTitle = MakeLabel("Configura el bucle", 0, 0, 0, 0, 11.5F, FontStyle.Bold, Foreground);
            loopPanel.Controls.Add(loopTitle);
            loopModes = new Panel { BackColor = UiColors.Input, TabIndex = 0 };
            repetitionsMode = MakeMode("Repeticiones", 0, 0, 0, true);
            durationMode = MakeMode("Duración rápida", 0, 0, 0, false);
            repetitionsMode.TabIndex = 0; durationMode.TabIndex = 1;
            loopModes.Controls.AddRange(new Control[] { repetitionsMode, durationMode });
            loopPanel.Controls.Add(loopModes);

            count = MakeNumber(0, 0, 100, 1, 1000, 3, "Número total de reproducciones");
            repetitionsFields = new Panel { BackColor = UiColors.Panel, TabIndex = 1 };
            Label countLabel = MakeLabel("Reproducciones", 0, 0, 200, 18, 8.5F, FontStyle.Regular, Muted);
            Control countFrame = FrameInput(count);
            Label countHelp = MakeLabel("veces, incluido el original", 114, 20, 340, 34, 9F, FontStyle.Regular, Muted);
            repetitionsFields.Controls.AddRange(new Control[] { countLabel, countFrame, countHelp });
            loopPanel.Controls.Add(repetitionsFields);

            durationFields = new Panel { BackColor = UiColors.Panel, TabIndex = 2 };
            durationHours = MakeNumber(0, 0, 96, 0, 9999, 1, "Horas de duración");
            durationMinutes = MakeNumber(0, 0, 76, 0, 59, 0, "Minutos de duración");
            durationSeconds = MakeNumber(0, 0, 76, 0, 59, 0, "Segundos de duración");
            foreach (Control field in new[] { CreateField("Horas", durationHours), CreateField("Minutos", durationMinutes), CreateField("Segundos", durationSeconds) })
                durationFields.Controls.Add(field);
            loopPanel.Controls.Add(durationFields);
            compressOnce = new OptionCheckBox { Text = "Comprimir el clip una vez antes de repetir", BackColor = UiColors.Panel,
                Font = new Font("Segoe UI", 9F), TabIndex = 3 };
            loopHint = MakeLabel("Cada reproducción conserva el vídeo completo.", 0, 0, 0, 0, 9F, FontStyle.Regular, Muted);
            loopPanel.Controls.AddRange(new Control[] { compressOnce, loopHint });
            workspace.Controls.Add(loopPanel);

            compressionPanel = new SurfacePanel { TabIndex = 2 };
            compressionTitle = MakeLabel("Compresión y tamaño", 0, 0, 0, 0, 11.5F, FontStyle.Bold, Foreground);
            compressionPanel.Controls.Add(compressionTitle);
            compressionProfile = MakeCombo(0, 0, 0, "Perfil de compresión", new[] { "Alta calidad", "Equilibrado", "Archivo pequeño", "Personalizado" });
            codecChoice = MakeCombo(0, 0, 0, "Códec de vídeo", new[] { "H.264", "H.265 / HEVC" });
            resolutionChoice = MakeCombo(0, 0, 0, "Resolución máxima", new[] { "Original", "1920 × 1080", "1280 × 720", "854 × 480" });
            fpsChoice = MakeCombo(0, 0, 0, "Máximo de fotogramas por segundo", new[] { "Original", "Máximo 30 fps", "Máximo 24 fps" });
            rateChoice = MakeCombo(0, 0, 0, "Control de calidad", new[] { "Calidad alta", "Equilibrada", "Más compresión", "Bitrate manual" });
            videoBitrate = MakeNumber(0, 0, 0, 64, 200000, 4000, "Bitrate de vídeo en kilobits por segundo");
            audioChoice = MakeCombo(0, 0, 0, "Compresión de audio", new[] { "AAC · 192 kbps", "AAC · 128 kbps", "AAC · 96 kbps", "AAC · 64 kbps", "Sin audio" });
            string[] names = { "Perfil", "Códec de vídeo", "Resolución máx.", "Fotogramas (fps)", "Control de calidad", "Vídeo (kbps)", "Audio" };
            Control[] options = { compressionProfile, codecChoice, resolutionChoice, fpsChoice, rateChoice, videoBitrate, audioChoice };
            for (int index = 0; index < options.Length; index++)
            {
                Panel field = CreateField(names[index], options[index]);
                field.TabIndex = index;
                optionFields.Add(field);
                compressionPanel.Controls.Add(field);
            }
            compressionHint = MakeLabel("", 0, 0, 0, 0, 8.5F, FontStyle.Regular, Muted);
            compressionHint.TextAlign = ContentAlignment.TopLeft;
            compressionPanel.Controls.Add(compressionHint);
            tips.SetToolTip(videoBitrate, "Bitrate objetivo de vídeo. 1000 kbps = 1 Mbps. El audio se suma a este valor.");
            tips.SetToolTip(codecChoice, "H.264 ofrece mayor compatibilidad. HEVC puede reducir el tamaño del archivo.");
            workspace.Controls.Add(compressionPanel);

            exportPanel = new SurfacePanel { TabIndex = 3 };
            exportTitle = MakeLabel("Codificación", 0, 0, 0, 0, 11.5F, FontStyle.Bold, Foreground);
            engineModes = new Panel { BackColor = UiColors.Input, TabIndex = 0 };
            cpuMode = MakeMode("CPU", 0, 0, 0, true);
            gpuMode = MakeMode("GPU", 0, 0, 0, false);
            cpuMode.TabIndex = 0; gpuMode.TabIndex = 1;
            engineModes.Controls.AddRange(new Control[] { cpuMode, gpuMode });
            tips.SetToolTip(gpuMode, "Detecta automáticamente una GPU AMD o NVIDIA compatible con el códec elegido.");
            gpuStatus = MakeLabel("Detectando GPU…", 0, 0, 0, 0, 8.5F, FontStyle.Regular, Muted);
            gpuHelp = MakeLabel("", 0, 0, 0, 0, 9F, FontStyle.Regular, Muted);
            gpuHelp.TextAlign = ContentAlignment.TopLeft;
            engineDivider = new Panel { BackColor = UiColors.Border };
            durationCaption = MakeLabel("Duración del resultado", 0, 0, 0, 0, 9F, FontStyle.Regular, Muted);
            summaryDuration = MakeLabel("—", 0, 0, 0, 0, 25F, FontStyle.Bold, Foreground);
            estimate = MakeLabel("Añade un vídeo para calcularla.", 0, 0, 0, 0, 8.5F, FontStyle.Regular, Accent);
            estimate.TextAlign = ContentAlignment.TopLeft;
            outputDivider = new Panel { BackColor = UiColors.Border };
            outputTitle = MakeLabel("Archivo de salida", 0, 0, 0, 0, 10F, FontStyle.Bold, Foreground);
            destinationFrame = new FieldFrame { TabIndex = 1 };
            outputFileName = MakeLabel("Sin vídeo seleccionado", 0, 0, 0, 0, 9F, FontStyle.Regular, Foreground);
            outputFileName.AutoEllipsis = true;
            destination = new TextBox { BackColor = UiColors.Input, ForeColor = Muted, BorderStyle = BorderStyle.None,
                ReadOnly = true, AccessibleName = "Archivo de salida", Font = new Font("Segoe UI", 8F), TabIndex = 0 };
            destination.TextChanged += delegate
            {
                outputFileName.Text = String.IsNullOrEmpty(destination.Text) ? "Sin vídeo seleccionado" : System.IO.Path.GetFileName(destination.Text);
                destination.SelectionStart = 0;
                destination.SelectionLength = 0;
                tips.SetToolTip(destination, destination.Text);
                tips.SetToolTip(outputFileName, destination.Text);
            };
            destinationFrame.Controls.AddRange(new Control[] { outputFileName, destination });
            chooseDestination = MakeButton("Cambiar destino", 0, 0, 0, 34, false);
            chooseDestination.TabIndex = 2;
            chooseDestination.Click += delegate { BrowseDestination(); };
            exportPanel.Controls.AddRange(new Control[] { exportTitle, engineModes, gpuStatus, gpuHelp, engineDivider,
                durationCaption, summaryDuration, estimate, outputDivider, outputTitle, destinationFrame, chooseDestination });
            workspace.Controls.Add(exportPanel);

            status = MakeLabel("Buscando FFmpeg en tu equipo…",
                0, 0, 0, 0, 9F, FontStyle.Regular, Foreground);
            status.AutoEllipsis = true;
            outputHelp = MakeLabel("MP4 · H.264 · Audio AAC", 0, 0, 0, 0, 8.5F, FontStyle.Regular, Muted);
            outputHelp.TextAlign = ContentAlignment.TopLeft;
            progressText = MakeLabel("", 0, 0, 0, 0, 9F, FontStyle.Bold, Accent);
            progressText.TextAlign = ContentAlignment.MiddleRight;
            progress = new ProgressTrack();
            create = MakeButton("Crear vídeo", 0, 0, 0, 40, true);
            create.Click += async delegate
            {
                if (!ffmpegReady) await PrepareFfmpegAsync();
                else await StartExportAsync();
            };
            cancel = MakeButton("Cancelar", 0, 0, 0, 40, false);
            cancel.Click += delegate { CancelExport(); };
            showResult = MakeButton("Mostrar archivo", 0, 0, 0, 40, false);
            showResult.Click += delegate { ShowOutput(); };
            create.TabIndex = 0; cancel.TabIndex = 1; showResult.TabIndex = 2;
            footer.Controls.AddRange(new Control[] { status, outputHelp, progressText, progress, create, cancel, showResult });

            compressionProfile.SelectedIndexChanged += ProfileChanged;
            foreach (ComboBox option in new[] { codecChoice, resolutionChoice, fpsChoice, rateChoice, audioChoice })
                option.SelectedIndexChanged += CompressionOptionChanged;
            videoBitrate.ValueChanged += CompressionOptionChanged;
            compressOnce.CheckedChanged += ModeOrDurationChanged;
            foreach (RadioButton option in new[] { repetitionsMode, durationMode, cpuMode, gpuMode })
                option.CheckedChanged += ModeOrDurationChanged;
            foreach (NumericUpDown option in new[] { count, durationHours, durationMinutes, durationSeconds })
                option.ValueChanged += ModeOrDurationChanged;
            dropTitle.TextChanged += delegate { tips.SetToolTip(dropTitle, inputPath ?? dropTitle.Text); };
            FormClosing += HandleClosing;
            Shown += async delegate
            {
                FitToScreen(Screen.FromControl(this).WorkingArea);
                await PrepareFfmpegAsync();
            };
            RefreshSuggestion();
            UpdateControls();
            ResumeLayout(true);
            layoutReady = true;
            LayoutWorkspace();
            MinimumSize = new Size(D(640), D(480));
        }

        private static Control FrameInput(Control input)
        {
            FieldFrame frame = new FieldFrame();
            frame.Controls.Add(input);
            input.TabIndex = 0;
            return frame;
        }

        private static Panel CreateField(string text, Control input)
        {
            Panel field = new Panel { BackColor = UiColors.Panel };
            Label label = MakeLabel(text, 0, 0, 0, 0, 8.5F, FontStyle.Regular, Muted);
            field.Controls.Add(label);
            field.Controls.Add(input is NumericUpDown ? FrameInput(input) : input);
            input.TabIndex = 0;
            return field;
        }

        private int D(float value) { return (int)Math.Round(value * uiScale); }
        private void Place(Control control, float x, float y, float width, float height)
        {
            control.SetBounds(D(x), D(y), Math.Max(0, D(width)), Math.Max(0, D(height)));
        }

        private void LayoutField(Panel field, float x, float y, float width)
        {
            Place(field, x, y, width, 58);
            Place(field.Controls[0], 0, 0, width, 18);
            Place(field.Controls[1], 0, 23, width, 34);
            if (field.Controls[1] is FieldFrame) LayoutNumber((FieldFrame)field.Controls[1], width);
        }

        private void LayoutNumber(FieldFrame frame, float width)
        {
            Control input = frame.Controls[0];
            input.SetBounds(D(10), Math.Max(D(5), (D(34) - input.PreferredSize.Height) / 2), D(width - 20), input.PreferredSize.Height);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (layoutReady && !layingOut) LayoutWorkspace();
        }

        private void LayoutWorkspace()
        {
            if (layingOut) return;
            layingOut = true;
            try
            {
                uiScale = CurrentAutoScaleDimensions.Width / 96F;
                float width = ClientSize.Width / uiScale;
                bool compactFooter = width < 900;
                float footerHeight = compactFooter ? 134 : 98;
                Place(header, 0, 0, width, 76);
                footer.SetBounds(0, ClientSize.Height - D(footerHeight), ClientSize.Width, D(footerHeight));
                scrollHost.SetBounds(0, header.Bottom, ClientSize.Width, Math.Max(0, footer.Top - header.Bottom));
                float margin = width < 760 ? 16 : 24;
                Place(brandMark, margin, 17, 40, 40);
                Place(headerTitle, margin + 53, 13, 300, 30);
                Place(headerDescription, margin + 54, 44, Math.Max(180, width - 330), 20);
                readyBadge.Visible = width >= 720;
                Place(readyBadge, width - margin - 206, 25, 206, 26);

                // Reserve the vertical scrollbar gutter so adding it never causes a horizontal one.
                float contentWidth = Math.Max(280, (scrollHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth) / uiScale - margin * 2);
                bool columns = contentWidth >= 850;
                float leftWidth = columns ? contentWidth - 296 : contentWidth;
                int fieldColumns = leftWidth >= 620 ? 4 : leftWidth >= 480 ? 3 : 2;
                int fieldRows = (optionFields.Count + fieldColumns - 1) / fieldColumns;
                float compressionHeight = 54 + fieldRows * 66 + 48;
                float mainHeight = columns ? Math.Max(460, 210 + 16 + compressionHeight) : 210 + 16 + compressionHeight + 16 + 460;
                float contentHeight = 88 + 18 + mainHeight;
                workspace.SetBounds(D(margin), D(8) + scrollHost.AutoScrollPosition.Y, D(contentWidth), D(contentHeight));
                scrollHost.AutoScrollMinSize = new Size(0, D(contentHeight + 26));

                Place(dropZone, 0, 0, contentWidth, 88);
                Place(fileMark, 18, 18, 50, 50);
                Place(dropTitle, 84, 18, contentWidth - 266, 26);
                Place(dropDetails, 84, 48, contentWidth - 266, 22);
                Place(chooseVideo, contentWidth - 158, 26, 140, 36);

                Place(loopPanel, 0, 106, leftWidth, 210);
                Place(loopTitle, 18, 15, leftWidth - 36, 26);
                Place(loopModes, 18, 52, leftWidth - 36, 38);
                float modeWidth = (leftWidth - 44) / 2;
                Place(repetitionsMode, 3, 3, modeWidth, 32);
                Place(durationMode, modeWidth + 5, 3, modeWidth, 32);
                Place(repetitionsFields, 18, 107, leftWidth - 36, 58);
                Place(repetitionsFields.Controls[0], 0, 0, leftWidth - 36, 18);
                Place(repetitionsFields.Controls[1], 0, 23, 100, 34);
                LayoutNumber((FieldFrame)repetitionsFields.Controls[1], 100);
                Place(repetitionsFields.Controls[2], 114, 23, leftWidth - 150, 34);
                Place(durationFields, 18, 107, leftWidth - 36, 58);
                float timeWidth = Math.Min(126, (leftWidth - 60) / 3);
                for (int i = 0; i < 3; i++) LayoutField((Panel)durationFields.Controls[i], i * (timeWidth + 12), 0, timeWidth);
                Place(compressOnce, 18, 174, leftWidth - 36, 24);
                Place(loopHint, 18, 174, leftWidth - 36, 24);

                Place(compressionPanel, 0, 332, leftWidth, compressionHeight);
                Place(compressionTitle, 18, 15, leftWidth - 36, 26);
                float fieldWidth = (leftWidth - 36 - (fieldColumns - 1) * 12) / fieldColumns;
                for (int i = 0; i < optionFields.Count; i++)
                    LayoutField(optionFields[i], 18 + (i % fieldColumns) * (fieldWidth + 12), 54 + (i / fieldColumns) * 66, fieldWidth);
                Place(compressionHint, 18, 58 + fieldRows * 66, leftWidth - 36, 38);

                float exportHeight = columns ? mainHeight : 460;
                Place(exportPanel, columns ? leftWidth + 16 : 0,
                    columns ? 106 : 332 + compressionHeight + 16, columns ? 280 : contentWidth, exportHeight);
                float exportWidth = columns ? 280 : contentWidth;
                float inside = exportWidth - 36;
                Place(exportTitle, 18, 15, inside, 26);
                Place(engineModes, 18, 52, inside, 38);
                float engineWidth = (inside - 8) / 2;
                Place(cpuMode, 3, 3, engineWidth, 32);
                Place(gpuMode, engineWidth + 5, 3, engineWidth, 32);
                Place(gpuStatus, 18, 98, inside, 34);
                Place(gpuHelp, 18, 136, inside, 44);
                Place(engineDivider, 18, 190, inside, 1);
                Place(durationCaption, 18, 204, inside, 20);
                Place(summaryDuration, 16, 224, inside + 2, 44);
                Place(estimate, 18, 276, inside, 35);
                float outputY = exportHeight - 140;
                Place(outputDivider, 18, outputY - 7, inside, 1);
                Place(outputTitle, 18, outputY + 6, inside, 20);
                Place(destinationFrame, 18, outputY + 34, inside, 50);
                Place(outputFileName, 10, 5, inside - 20, 21);
                Place(destination, 10, 28, inside - 20, 18);
                Place(chooseDestination, 18, outputY + 96, inside, 32);

                float buttonY = compactFooter ? 78 : 22;
                Place(create, width - margin - 156, buttonY, 156, 40);
                Place(cancel, width - margin - 262, buttonY, 96, 40);
                Place(showResult, compactFooter ? margin : width - margin - 418, buttonY, 146, 40);
                float statusWidth = compactFooter ? width - 2 * margin - 80 : width - 2 * margin - 448;
                Place(status, margin, 14, Math.Max(100, statusWidth - 8), 24);
                Place(progressText, compactFooter ? width - margin - 76 : margin + statusWidth - 76, compactFooter ? 14 : 52, 76, 24);
                Place(outputHelp, margin, 43, compactFooter ? width - 2 * margin : statusWidth - 88, 31);
                Place(progress, margin, footerHeight - 10, width - margin * 2, 4);
            }
            finally { layingOut = false; }
        }

        private void FitToScreen(Rectangle workingArea)
        {
            int availableWidth = Math.Max(320, workingArea.Width - 32);
            int availableHeight = Math.Max(320, workingArea.Height - 32);
            MinimumSize = new Size(Math.Min(D(640), availableWidth), Math.Min(D(480), availableHeight));
            Size = new Size(Math.Min(Width, availableWidth), Math.Min(Height, availableHeight));
            Location = new Point(workingArea.Left + (workingArea.Width - Width) / 2,
                workingArea.Top + (workingArea.Height - Height) / 2);
            LayoutWorkspace();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        private static Icon CreateProductIcon()
        {
            using (Bitmap bitmap = new Bitmap(64, 64))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    UiPaint.Surface(graphics, new Rectangle(1, 1, 62, 62), Accent, Accent, 14);
                    UiPaint.Loop(graphics, new RectangleF(10, 19, 44, 26), Background);
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (tips != null) tips.Dispose();
                if (productIcon != null) productIcon.Dispose();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
