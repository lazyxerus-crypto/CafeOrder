using ImageMagick;

namespace CafeOrder;

internal static class ManualImages
{
    public static void Save(string source, string destination)
    {
        // Read file bytes rather than interpreting a file name as an ImageMagick command.
        using var image = new MagickImage(File.ReadAllBytes(source));
        image.AutoOrient(); uint side = Math.Min(image.Width, image.Height);
        image.Crop(side, side, Gravity.Center); image.ResetPage(); image.Strip(); image.Quality = 80;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string pending = destination + ".tmp";
        try { image.Write(pending, MagickFormat.WebP); File.Move(pending, destination, true); }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
    public static Image Load(string path)
    {
        using var image = new MagickImage(File.ReadAllBytes(path));
        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        using var bitmap = Image.FromStream(stream); return new Bitmap(bitmap);
    }
}
