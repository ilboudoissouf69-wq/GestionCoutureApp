using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// Compresse et redimensionne une photo à la source :
    ///   - Taille max 1024 × 768 (ratio conservé)
    ///   - Format JPEG qualité 70 %
    ///   - Résultat : ~50–100 Ko au lieu de 4–6 Mo
    /// Appelé systématiquement après chaque import fichier ou capture webcam.
    /// </summary>
    public static class PhotoCompressor
    {
        private const int LargeurMax  = 1024;
        private const int HauteurMax  = 768;
        private const long QualiteJpeg = 70L;   // 0–100

        /// <summary>
        /// Compresse <paramref name="cheminSource"/> et écrit le résultat
        /// dans <paramref name="cheminDestination"/> (peut être identique).
        /// Retourne le chemin destination.
        /// </summary>
        public static string Compresser(string cheminSource, string cheminDestination)
        {
            using var original = new Bitmap(cheminSource);

            // Calculer la nouvelle taille en conservant le ratio
            var (largeur, hauteur) = CalculerTaille(original.Width, original.Height);

            using var redimensionne = new Bitmap(largeur, hauteur);
            using (var g = Graphics.FromImage(redimensionne))
            {
                g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode      = SmoothingMode.HighQuality;
                g.DrawImage(original, 0, 0, largeur, hauteur);
            }

            // Encoder JPEG avec la qualité cible
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, QualiteJpeg);
            var codec = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

            // Écriture dans un buffer mémoire puis sur disque (évite de lire
            // et écrire le même fichier simultanément si source == destination)
            using var ms = new MemoryStream();
            redimensionne.Save(ms, codec, encoderParams);
            ms.Position = 0;

            File.WriteAllBytes(cheminDestination, ms.ToArray());
            return cheminDestination;
        }

        /// <summary>
        /// Compresse un <see cref="System.Drawing.Bitmap"/> en mémoire
        /// (utilisé après une capture webcam) et enregistre le résultat
        /// dans <paramref name="cheminDestination"/>.
        /// </summary>
        public static string CompresserBitmap(System.Drawing.Bitmap bitmap, string cheminDestination)
        {
            var (largeur, hauteur) = CalculerTaille(bitmap.Width, bitmap.Height);

            using var redimensionne = new Bitmap(largeur, hauteur);
            using (var g = Graphics.FromImage(redimensionne))
            {
                g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode      = SmoothingMode.HighQuality;
                g.DrawImage(bitmap, 0, 0, largeur, hauteur);
            }

            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, QualiteJpeg);
            var codec = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

            using var ms = new MemoryStream();
            redimensionne.Save(ms, codec, encoderParams);
            ms.Position = 0;

            // Forcer l'extension .jpg
            string dest = Path.ChangeExtension(cheminDestination, ".jpg");
            File.WriteAllBytes(dest, ms.ToArray());
            return dest;
        }

        private static (int largeur, int hauteur) CalculerTaille(int w, int h)
        {
            if (w <= LargeurMax && h <= HauteurMax)
                return (w, h);   // déjà assez petite, pas de redimensionnement

            double ratioW = (double)LargeurMax / w;
            double ratioH = (double)HauteurMax / h;
            double ratio  = Math.Min(ratioW, ratioH);

            return ((int)(w * ratio), (int)(h * ratio));
        }
    }
}
