using ImageMagick;

namespace CafeOrder;

internal static class ManualImages
{
    public static void Save(string source, string destination)
        => Save(File.ReadAllBytes(source), destination);

    public static void Save(byte[] source, string destination)
    {
        using var image = new MagickImage(source);
        image.AutoOrient(); uint side = Math.Min(image.Width, image.Height);
        image.Crop(side, side, Gravity.Center); image.ResetPage();
        if (side > 800) image.Resize(800, 800);
        image.Strip(); image.Quality = 80;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string pending = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
