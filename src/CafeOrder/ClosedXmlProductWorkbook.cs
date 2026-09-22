using System.Globalization;
using ClosedXML.Excel;
using ClosedXML.Graphics;

namespace CafeOrder;

internal sealed class ClosedXmlProductWorkbook : IProductWorkbook
{
    static ClosedXmlProductWorkbook()
    {
        // Restrict font lookup to a Windows font stream. Enumerating per-user fonts can fail on POS accounts.
        string directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string? path = new[] { "malgun.ttf", "arial.ttf", "segoeui.ttf" }
            .Select(name => System.IO.Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        if (path == null) throw new InvalidDataException("Windows 글꼴을 찾을 수 없어 XLSX를 열 수 없습니다.");
        using var font = File.OpenRead(path);
        LoadOptions.DefaultGraphicEngine = DefaultGraphicEngine.CreateOnlyWithFonts(font);
    }
    internal static readonly string[] Headers = ["ProductId", "Supplier", "Name", "Price", "DisplayPrice", "Category", "ProductUrl", "IsActive"];
    private readonly IReadOnlyList<Supplier> suppliers;
    private readonly IReadOnlyList<string> categories;
    internal ClosedXmlProductWorkbook(IReadOnlyList<Supplier> suppliers, IReadOnlyList<string> categories)
    { this.suppliers = suppliers; this.categories = categories; }

    public void Export(string xlsxPath, IReadOnlyList<ProductTransferRow> products)
    {
        if (!string.Equals(System.IO.Path.GetExtension(xlsxPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(".xlsx 파일을 선택해주세요.");
        string pending = xlsxPath + ".pending-" + Guid.NewGuid().ToString("N") + ".xlsx";
        try
        {
            using var book = new XLWorkbook(); var sheet = book.Worksheets.Add("Products");
            for (int col = 0; col < Headers.Length; col++) sheet.Cell(1, col + 1).Value = Headers[col];
            sheet.Range(1, 1, 1, Headers.Length).Style.Font.Bold = true;
            sheet.Column(1).Width = 13; sheet.Column(2).Width = 20; sheet.Column(3).Width = 46;
            sheet.Column(4).Width = 16; sheet.Column(5).Width = 28; sheet.Column(6).Width = 20;
            sheet.Column(7).Width = 58; sheet.Column(8).Width = 13;
            for (int index = 0; index < products.Count; index++)
            {
                var product = products[index]; int row = index + 2;
                if (product.ProductId is int id) sheet.Cell(row, 1).Value = id;
                sheet.Cell(row, 2).Value = product.Supplier;
                sheet.Cell(row, 3).Value = product.Name;
                sheet.Cell(row, 4).Value = (double)product.Price;
                sheet.Cell(row, 5).Value = product.DisplayPrice;
                sheet.Cell(row, 6).Value = product.Category;
                sheet.Cell(row, 7).Value = product.ProductUrl;
                sheet.Cell(row, 8).Value = product.IsActive;
            }
            book.SaveAs(pending);
            File.Move(pending, xlsxPath, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public ProductWorkbookRead Import(string xlsxPath)
    {
        if (!string.Equals(System.IO.Path.GetExtension(xlsxPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(".xlsx 파일을 선택해주세요.");
        XLWorkbook book;
        try { book = new XLWorkbook(xlsxPath); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new InvalidDataException("XLSX 파일을 읽을 수 없습니다. 파일 형식을 확인해주세요.", ex); }
        using (book)
        {
            var issues = new List<ProductWorkbookIssue>(); var rows = new List<ProductTransferRow>();
            var sheet = book.Worksheets.FirstOrDefault();
            if (sheet == null) return new(rows, [new(1, "시트", "상품 시트가 없습니다.")]);
            for (int col = 1; col <= Headers.Length; col++)
                if (sheet.Cell(1, col).HasFormula || sheet.Cell(1, col).GetString().Trim() != Headers[col - 1])
                    issues.Add(new(1, Headers[col - 1], $"{col}번째 머리글이 '{Headers[col - 1]}'이어야 합니다."));
            if (sheet.LastColumnUsed()?.ColumnNumber() > Headers.Length)
                issues.Add(new(1, "열", "8개 지정 열 밖에 데이터가 있습니다."));
            if (issues.Count != 0) return new(rows, issues);
            int last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= last; row++)
            {
                var cells = Enumerable.Range(1, Headers.Length).Select(col => sheet.Cell(row, col)).ToArray();
                if (cells.All(cell => cell.IsEmpty())) continue;
                int before = issues.Count;
                for (int col = 0; col < cells.Length; col++)
                    if (cells[col].HasFormula || cells[col].DataType == XLDataType.Error)
                        issues.Add(new(row, Headers[col], "수식/오류 셀 대신 값을 입력해주세요."));
                if (issues.Count != before) continue;
                int? id = null; string idText = cells[0].GetString().Trim();
                if (idText.Length != 0)
                {
                    if (!TryNumber(cells[0], out decimal parsedId) || parsedId <= 0 || parsedId > int.MaxValue || decimal.Truncate(parsedId) != parsedId)
                        issues.Add(new(row, "ProductId", "양의 정수이거나 신규 상품이면 빈칸이어야 합니다."));
                    else id = (int)parsedId;
                }
                string supplier = cells[1].GetString().Trim(), name = cells[2].GetString();
                if (!suppliers.Any(item => item.Name == supplier || string.Equals(item.Id, supplier, StringComparison.OrdinalIgnoreCase)))
                    issues.Add(new(row, "Supplier", "등록된 판매처만 입력해주세요."));
                if (string.IsNullOrWhiteSpace(name)) issues.Add(new(row, "Name", "상품명을 입력해주세요."));
                if (!TryNumber(cells[3], out decimal price) || price < 0)
                    issues.Add(new(row, "Price", "0 이상의 숫자여야 합니다."));
                string display = cells[4].GetString(), category = cells[5].GetString().Trim(), url = cells[6].GetString().Trim();
                if (!categories.Skip(1).Contains(category)) issues.Add(new(row, "Category", "고정 카테고리 중 하나를 입력해주세요."));
                if (url.Length != 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) ||
                    (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps) ||
                    parsedUrl.Host.Length == 0 || parsedUrl.UserInfo.Length != 0))
                    issues.Add(new(row, "ProductUrl", "비워두거나 정상적인 http/https URL을 입력해주세요."));
                bool active = false;
                if (cells[7].DataType == XLDataType.Boolean) active = cells[7].GetBoolean();
                else if (cells[7].DataType != XLDataType.Text || !bool.TryParse(cells[7].GetString().Trim(), out active))
                    issues.Add(new(row, "IsActive", "true 또는 false만 입력해주세요."));
                if (issues.Count == before) rows.Add(new(id, supplier, name, price, display, category, url, active, row));
            }
            return new(rows, issues);
        }
    }
    private static bool TryNumber(IXLCell cell, out decimal value)
    {
        value = 0;
        return cell.DataType switch
        {
            XLDataType.Number => cell.TryGetValue(out value),
            XLDataType.Text => decimal.TryParse(cell.GetString().Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }
}
