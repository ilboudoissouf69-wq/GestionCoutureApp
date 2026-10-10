using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;

// Alias explicites pour lever l'ambiguïté OpenCvSharp.Window vs System.Windows.Window
using CvWindow  = OpenCvSharp.Window;
using WpfWindow = System.Windows.Window;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Fenêtre de capture webcam — remplace AForge 2013 par OpenCvSharp4 (2024).
    /// OpenCvSharp4.Windows utilise OpenCV 4.x natif sans COM DirectShow.
    /// Compatible .NET 8 x64 Windows sans NU1701.
    /// </summary>
    public partial class WebcamCaptureWindow : WpfWindow
    {
        private VideoCapture?    _capture;
        private DispatcherTimer? _timer;
        private Mat?             _lastFrame;
        private readonly object  _verrouFrame = new();

        public string? CapturedFilePath { get; private set; }

        public WebcamCaptureWindow()
        {
            InitializeComponent();
        }

        // ──────────────────────────────────────────────────────────
        // Démarrer la caméra
        // ──────────────────────────────────────────────────────────
        private void BtnDemarrer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _capture = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
                if (!_capture.IsOpened())
                {
                    _capture.Dispose();
                    _capture = new VideoCapture(0, VideoCaptureAPIs.ANY);
                }

                if (!_capture.IsOpened())
                {
                    MessageBox.Show(
                        "Aucune webcam détectée ou webcam déjà utilisée par une autre application.",
                        "Webcam introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _capture.Dispose();
                    _capture = null;
                    return;
                }

                _capture.Set(VideoCaptureProperties.FrameWidth,  640);
                _capture.Set(VideoCaptureProperties.FrameHeight, 480);

                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _timer.Tick += TimerTick;
                _timer.Start();

                BtnDemarrer.IsEnabled = false;
                BtnCapturer.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Impossible d'ouvrir la webcam :\n" + ex.Message,
                    "Erreur webcam", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ──────────────────────────────────────────────────────────
        // Lecture de chaque frame (DispatcherTimer ~20 fps)
        // ──────────────────────────────────────────────────────────
        private void TimerTick(object? sender, EventArgs e)
        {
            if (_capture == null || !_capture.IsOpened()) return;

            var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty())
            { frame.Dispose(); return; }

            // BGR → BitmapSource WPF
            BitmapSource? bmp = null;
            try { bmp = MatToBitmapSource(frame); }
            catch { frame.Dispose(); return; }

            lock (_verrouFrame)
            {
                _lastFrame?.Dispose();
                _lastFrame = frame;
            }

            if (bmp != null)
                WebcamPreview.Source = bmp;
        }

        // ──────────────────────────────────────────────────────────
        // Convertir un Mat BGR en BitmapSource WPF sans WpfExtensions
        // ──────────────────────────────────────────────────────────
        private static BitmapSource MatToBitmapSource(Mat mat)
        {
            // Convertir BGR → RGB
            var rgb = new Mat();
            Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);

            int width    = rgb.Width;
            int height   = rgb.Height;
            int channels = rgb.Channels();
            int stride   = width * channels;

            // Récupérer les pixels
            var pixelData = new byte[height * stride];
            System.Runtime.InteropServices.Marshal.Copy(rgb.Data, pixelData, 0, pixelData.Length);
            rgb.Dispose();

            var bmp = BitmapSource.Create(
                width, height,
                96, 96,
                PixelFormats.Rgb24,
                null,
                pixelData,
                stride);
            bmp.Freeze();
            return bmp;
        }

        // ──────────────────────────────────────────────────────────
        // Capturer la frame courante → JPEG sur disque
        // ──────────────────────────────────────────────────────────
        private void BtnCapturer_Click(object sender, RoutedEventArgs e)
        {
            Mat? copie;
            lock (_verrouFrame)
            {
                if (_lastFrame == null || _lastFrame.Empty()) return;
                copie = _lastFrame.Clone();
            }

            try
            {
                string dossier = GestionCoutureApp.Helpers.AppPaths.DossierPhotos;
                string suffixe = Guid.NewGuid().ToString("N")[..8];
                string chemin  = Path.Combine(dossier,
                    $"photo_{DateTime.Now:yyyyMMdd_HHmmss}_{suffixe}.jpg");

                // Qualité JPEG 85
                Cv2.ImWrite(chemin, copie,
                    new ImageEncodingParam(ImwriteFlags.JpegQuality, 85));

                CapturedFilePath = chemin;
                DialogResult     = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la capture :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                copie?.Dispose();
            }
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            CapturedFilePath = null;
            DialogResult     = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            _timer = null;

            lock (_verrouFrame)
            {
                _lastFrame?.Dispose();
                _lastFrame = null;
            }

            _capture?.Release();
            _capture?.Dispose();
            _capture = null;

            base.OnClosed(e);
        }
    }
}
