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

        // CORRECTIF (sécurité multithread) : AForge livre les frames sur son
        // propre thread de capture, pendant que BtnCapturer_Click lit _lastFrame
        // sur le thread UI. Sans verrou, une frame peut être disposée par
        // VideoSource_NewFrame exactement pendant que BtnCapturer_Click est en
        // train de l'enregistrer sur disque (ObjectDisposedException possible,
        // rare mais réelle en usage normal vu la fréquence des frames).
        private readonly object _verrouFrame = new();

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
            // CORRECTIF (fuite de ressources GDI+) : à chaque frame (potentiellement
            // 15 à 30 fois par seconde), l'ancien code écrasait _lastFrame par un
            // nouveau Bitmap SANS jamais disposer le précédent. Un Bitmap GDI+
            // encapsule un handle système non géré : laisser la fenêtre webcam
            // ouverte plus de quelques secondes épuisait progressivement ces handles,
            // avec un comportement de plus en plus instable pour toute l'application.
            var nouvelleFrame = (Bitmap)eventArgs.Frame.Clone();
            Bitmap? ancienneFrame;
            lock (_verrouFrame)
            {
                ancienneFrame = _lastFrame;
                _lastFrame = nouvelleFrame;
            }
            ancienneFrame?.Dispose();

            Dispatcher.Invoke(() =>
            {
                WebcamPreview.Source = BitmapToImageSource(nouvelleFrame);
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
            Bitmap? copieLocale;
            lock (_verrouFrame)
            {
                if (_lastFrame == null) return;
                // Copie sous verrou : on ne travaille plus ensuite sur le champ
                // partagé, qui peut continuer à être réassigné/disposé par le
                // thread de capture pendant qu'on écrit le fichier sur disque.
                copieLocale = (Bitmap)_lastFrame.Clone();
            }

            try
            {
                // CORRECTIF : dossier AppData centralisé + nom de fichier
                // unique (voir la même correction dans CommandesView.cs).
                string dossierPhotos = GestionCoutureApp.Helpers.AppPaths.DossierPhotos;

                // CORRECTIF (bug réel) : l'ancien code tronquait la chaîne complète
                // ("photo_" + horodatage + GUID) à 24 caractères AVANT d'ajouter
                // l'extension. "photo_" (6) + horodatage yyyyMMdd_HHmmss (15) + "_" (1)
                // = 22 caractères déjà utilisés, ce qui ne laissait que 2 caractères
                // hexadécimaux du GUID (256 combinaisons) — loin de l'unicité recherchée.
                // On prend maintenant explicitement 8 caractères du GUID.
                string suffixeUnique = Guid.NewGuid().ToString("N")[..8];
                string chemin = System.IO.Path.Combine(dossierPhotos,
                    $"photo_{DateTime.Now:yyyyMMdd_HHmmss}_{suffixeUnique}.jpg");
                copieLocale.Save(chemin, ImageFormat.Jpeg);
                CapturedFilePath = chemin;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur capture : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                copieLocale.Dispose();
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
            lock (_verrouFrame)
            {
                _lastFrame?.Dispose();
                _lastFrame = null;
            }
            base.OnClosed(e);
        }
    }
}
