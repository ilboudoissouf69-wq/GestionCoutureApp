using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;

namespace GestionCoutureApp.Views
{
    public partial class WebcamCaptureWindow : Window
    {
        private VideoCaptureDevice? _videoSource;
        private Bitmap? _lastFrame;

        public string? CapturedFilePath { get; private set; }

        public WebcamCaptureWindow()
        {
            InitializeComponent();
        }

        private void BtnDemarrer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (videoDevices.Count == 0)
                {
                    MessageBox.Show("Aucune webcam detectee.",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
                _videoSource.NewFrame += VideoSource_NewFrame;
                _videoSource.Start();

                BtnDemarrer.IsEnabled = false;
                BtnCapturer.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur webcam : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            _lastFrame = (Bitmap)eventArgs.Frame.Clone();
            Dispatcher.Invoke(() =>
            {
                WebcamPreview.Source = BitmapToImageSource(_lastFrame);
            });
        }

        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void BtnCapturer_Click(object sender, RoutedEventArgs e)
        {
            if (_lastFrame == null) return;

            try
            {
                string dossierPhotos = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "photos");
                if (!Directory.Exists(dossierPhotos))
                    Directory.CreateDirectory(dossierPhotos);

                string chemin = System.IO.Path.Combine(dossierPhotos,
                    $"photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                _lastFrame.Save(chemin, ImageFormat.Jpeg);
                CapturedFilePath = chemin;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur capture : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            CapturedFilePath = null;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.NewFrame -= VideoSource_NewFrame;
                _videoSource = null;
            }
            _lastFrame?.Dispose();
            base.OnClosed(e);
        }
    }
}
