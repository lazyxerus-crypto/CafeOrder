using System.Drawing.Imaging;

namespace CafeOrder;

internal static class ImageDropData
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    internal static bool CanAccept(IDataObject? data) => data != null &&
        (data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap) ||
         data.GetDataPresent("FileContents"));

    internal static byte[] Read(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
            Extensions.Contains(Path.GetExtension(files[0])) && File.Exists(files[0]))
        {
            return File.ReadAllBytes(files[0]);
        }
        if (data?.GetData(DataFormats.Bitmap) is Image bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        if (data?.GetData("FileContents") is Stream contents)
        {
            using var stream = new MemoryStream();
            contents.CopyTo(stream);
            if (stream.Length is < 1 or > 25_000_000)
                throw new InvalidDataException("이미지 데이터의 크기를 확인할 수 없습니다.");
            return stream.ToArray();
        }
        throw new InvalidDataException("이미지 파일이나 이미지 데이터가 없습니다.");
    }
}
