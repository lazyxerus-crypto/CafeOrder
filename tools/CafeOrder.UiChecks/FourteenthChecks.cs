using CafeOrder;
using System.Drawing.Imaging;
using System.Reflection;

internal static partial class Program
{
    private static async Task CheckFourteenthAsync()
    {
        string directory = CheckDirectory("optional-fields");
        var data = new SampleData(new LocalState(directory));
        const string url = "https://brand.naver.com/macrocom/products/12639611652";
        var blank = data.SaveManualStoreProduct(url, "기타", new("", null, null), null, null, null);
        data.AddToCart(blank);
        Require(blank.Name == "" && blank.DisplayName == "이름 미입력" &&
            !blank.PriceKnown && blank.PriceText == "가격 미입력" &&
            blank.ManualImagePath == null && blank.ImageCachePath == null &&
            data.HasUnknownPrice(data.Cart) && data.Cart.Single().Quantity == 0,
            "URL-only manual entry preserves unknown name, price and photo without a sample value");
        var restarted = new SampleData(new LocalState(directory));
        var restored = restarted.Products.Single(p => p.Id == blank.Id);
        Require(restored.Name == "" && !restored.PriceKnown && restored.PriceText == "가격 미입력" &&
            restarted.Cart.Single().Product.Id == blank.Id && restarted.Cart.Single().Quantity == 0,
            "Unknown manual fields and cart quantity survive SQLite restart");

        string xlsx = Path.Combine(directory, "optional-fields.xlsx");
        data.ExportWorkbook(xlsx);
        var workbook = new ClosedXmlProductWorkbook(data.Suppliers, SampleData.Categories);
        var imported = workbook.Import(xlsx);
        Require(imported.Issues.Count == 0 && imported.Rows.Single().Name == "" &&
            !imported.Rows.Single().PriceKnown && !imported.Rows.Single().LookupRequested &&
            imported.Rows.Single().Price == 0 && imported.Rows.Single().DisplayPrice == "",
            "XLSX keeps empty manual name/price distinct from an actual zero price and does not force web lookup");
        using (var plan = await data.PrepareImportAsync(xlsx, _ => throw new Exception("No web lookup expected"),
            null, CancellationToken.None, manualStoreLookup: _ => throw new Exception("No web lookup expected")))
            Require(plan.Issues.Count == 0 && plan.Changes.Count == 0,
                "Importing the exported blank product leaves DB unchanged without a web request");

        var beforeId = blank.Id;
        data.ChangeName(blank, "직접 입력한 상품");
        data.ChangePrice(blank, 0);
        Require(blank.PriceKnown && blank.Price == 0 && blank.PriceText == "0원" &&
            !data.HasUnknownPrice(data.Cart), "Actual 0 won is a known price, unlike an empty price");
        data.ChangePrice(blank, null);
        Require(!blank.PriceKnown && blank.PriceText == "가격 미입력" && data.HasUnknownPrice(data.Cart),
            "Clearing the price restores the unknown state");
        blank.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow;
        data.Store.Database.SaveProduct(blank);
        const string changedUrl = "https://brand.naver.com/otokimall/products/11668648365";
        data.ChangeUrl(blank, changedUrl);
        Require(blank.Id == beforeId && blank.Name == "직접 입력한 상품" &&
            blank.Url == changedUrl && blank.LastSuccessfulCheckAtUtc == null &&
            data.Cart.Single().Quantity == 0, "Changing a URL preserves ID/name/cart and clears old lookup time");
        try { data.ChangeUrl(blank, "https://www.coupang.com/vp/products/6718538845?vendorItemId=88353437196");
            throw new Exception("Cross-supplier URL accepted"); }
        catch (ArgumentException) { }
        var duplicate = data.SaveManualStoreProduct(url, "기타", new("다른 상품", 100, null), null, null, null);
        try { data.ChangeUrl(duplicate, changedUrl + "?NaPm=other");
            throw new Exception("Duplicate product URL accepted"); }
        catch (ArgumentException) { }
        Require(duplicate.Url == url && blank.Url == changedUrl,
            "Rejected URL edits preserve both original products");

        using var source = new Bitmap(40, 30);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.CornflowerBlue);
        using var stream = new MemoryStream(); source.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        var bitmapData = new DataObject(); bitmapData.SetData(DataFormats.Bitmap, true, source);
        Require(ImageDropData.CanAccept(bitmapData) && ImageDropData.Read(bitmapData).Length > 100,
            "Browser-provided bitmap data can be dropped without downloading a URL");
        string sourcePath = Path.Combine(directory, "drop.png"); File.WriteAllBytes(sourcePath, bytes);
        var fileData = new DataObject(DataFormats.FileDrop, new[] { sourcePath });
        Require(ImageDropData.Read(fileData).SequenceEqual(bytes), "Explorer image drop reads the supplied file");
        string invalidPath = Path.Combine(directory, "bad.txt"); File.WriteAllText(invalidPath, "not an image");
        try { ImageDropData.Read(new DataObject(DataFormats.FileDrop, new[] { invalidPath }));
            throw new Exception("Unsupported image file accepted"); }
        catch (InvalidDataException) { }

        using (var dialog = new ManualStoreProductDialog("네이버 스마트스토어", changedUrl, blank, null, "HTTP 429"))
        {
            dialog.Show(); Application.DoEvents();
            var preview = All(dialog).OfType<PictureBox>().Single();
            Require(preview.AllowDrop && All(dialog).OfType<TextBox>().Single(box => box.MaxLength == 24).Text == "",
                "The entire preview accepts drops and an unknown price reopens as blank");
            var drop = new DragEventArgs(fileData, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragEnter", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(preview, [drop]);
            Require(drop.Effect == DragDropEffects.Copy, "Supported image highlights the preview drop target");
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(preview, [drop]);
            Require(preview.Image != null && preview.Image.Width == 40,
                "Dropped image replaces the manual entry preview");
            All(dialog).OfType<Button>().Single(button => button.Text == "저장").PerformClick();
            Require(dialog.Input?.ImageBytes?.SequenceEqual(bytes) == true && dialog.Input.Price == null,
                "Saving a dropped image preserves the unknown price");
        }
        using (var card = new ProductCard(blank, data))
        {
            card.SetManual(bytes);
            string retainedImage = blank.ManualImagePath!;
            byte[] retainedBytes = File.ReadAllBytes(retainedImage);
            try { card.SetManual(File.ReadAllBytes(invalidPath)); throw new Exception("Invalid image replaced a good photo"); }
            catch (ImageMagick.MagickException) { }
            Require(blank.ManualImagePath == retainedImage && File.ReadAllBytes(retainedImage).SequenceEqual(retainedBytes),
                "Invalid image data leaves the existing photo intact");
            Require(blank.Id == beforeId && blank.Name == "직접 입력한 상품" &&
                blank.Url == changedUrl && !blank.PriceKnown && File.Exists(blank.ManualImagePath),
                "Shared product-card drop processing only changes its manual image");
            using var menu = card.BuildMenu();
            Require(menu.Items.Cast<ToolStripItem>().Select(item => item is ToolStripSeparator ? "-" : item.Text)
                .SequenceEqual(["상품 삭제", "이미지 삭제", "카테고리 변경", "상품 링크", "-",
                    "이름 변경", "가격 변경", "링크 변경", "-", "이름 복사", "가격 복사", "이름 + 가격 복사"]),
                "Registered product menu has the requested order");
        }
        var final = new SampleData(new LocalState(directory));
        var finalProduct = final.Products.Single(p => p.Id == beforeId);
        Require(finalProduct.ManualImagePath != null && File.Exists(finalProduct.ManualImagePath) &&
            finalProduct.Name == "직접 입력한 상품" && !finalProduct.PriceKnown &&
            finalProduct.Url == changedUrl && final.Cart.Single().Quantity == 0,
            "Manual photo, unknown price, edited URL and quantity survive another restart");
    }
}
