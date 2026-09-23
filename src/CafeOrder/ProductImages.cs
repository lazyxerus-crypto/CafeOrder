namespace CafeOrder;

internal readonly record struct ProductImageSelection(Image Image, bool Owned, bool Manual);

internal static class ProductImages
{
    internal static string Identity(Product product) =>
        $"{product.ManualImagePath}|{product.ImageCachePath}|{product.Category}|" +
        $"{File.Exists(product.ManualImagePath)}/{File.Exists(product.ImageCachePath)}";

    internal static ProductImageSelection Resolve(Product product)
    {
        if (TryLoad(product.ManualImagePath) is { } manual) return new(manual, true, true);
        if (TryLoad(product.ImageCachePath) is { } web) return new(web, true, false);
        return new(SampleImages.Catalog(product.Category), false, false);
    }

    private static Image? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return ManualImages.Load(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            ImageMagick.MagickException or System.Runtime.InteropServices.ExternalException) { return null; }
    }
}
