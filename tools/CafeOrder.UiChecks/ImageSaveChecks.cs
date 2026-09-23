using CafeOrder;
using System.Drawing.Imaging;

internal static partial class Program
{
    private static void CheckImageSaveRules()
    {
        string directory = CheckDirectory("image-save");
        Directory.CreateDirectory(directory);
        foreach (var (width, height, expected) in new[]
        {
            (500, 900, 500), (1200, 600, 600), (2000, 1200, 800), (3000, 3000, 800)
        })
        {
            string source = Path.Combine(directory, $"source-{width}-{height}.png");
            string outputImage = Path.Combine(directory, $"saved-{width}-{height}.webp");
            using (var bitmap = new Bitmap(width, height))
            {
                using var graphic = Graphics.FromImage(bitmap);
                graphic.Clear(Color.CornflowerBlue);
                bitmap.Save(source, ImageFormat.Png);
            }
            ManualImages.Save(source, outputImage);
            using var saved = ManualImages.Load(outputImage);
            Require(saved.Width == expected && saved.Height == expected,
                $"Center-cropped {width}x{height} image saves as {expected}x{expected} WebP");
        }
        string transparent = Path.Combine(directory, "transparent.png");
        string webp = Path.Combine(directory, "transparent.webp");
        using (var bitmap = new Bitmap(500, 900, PixelFormat.Format32bppArgb))
        {
            using var graphic = Graphics.FromImage(bitmap);
            graphic.Clear(Color.Transparent);
            graphic.FillRectangle(Brushes.Red, 220, 420, 60, 60);
            bitmap.Save(transparent, ImageFormat.Png);
        }
        ManualImages.Save(transparent, webp);
        using (var saved = (Bitmap)ManualImages.Load(webp))
            Require(saved.Width == 500 && saved.Height == 500 && saved.GetPixel(0, 0).A == 0 &&
                saved.GetPixel(250, 250).A > 200, "Transparent pixels survive WebP crop without upscaling");
        byte[] original = File.ReadAllBytes(webp);
        try { ManualImages.Save([1, 2, 3], webp); throw new Exception("Broken image unexpectedly converted"); }
        catch (ImageMagick.MagickException) { }
        Require(File.ReadAllBytes(webp).SequenceEqual(original) &&
            !Directory.GetFiles(directory, "*.tmp").Any(), "Failed conversion preserves prior image and cleans temporary files");
        using var legacy = ManualImages.Load(transparent);
        Require(legacy.Width == 500 && legacy.Height == 900, "Older image formats remain readable");
        results.Add("Image crop, 800px cap, alpha, legacy read, and failed-write protection PASS");
    }
}
