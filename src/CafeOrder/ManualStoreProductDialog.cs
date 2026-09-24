using System.Globalization;

namespace CafeOrder;

internal sealed record ManualStoreProductInput(string Name, decimal? Price, string? ImageFile,
    byte[]? ImageBytes = null);

internal sealed class ManualStoreProductDialog : Form
{
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 500 };
    private readonly TextBox price = new() { Dock = DockStyle.Fill, MaxLength = 24 };
    private readonly TextBox imagePath = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(232, 238, 230) };
    private Image? ownedPreview;
    private byte[]? droppedImage;
    internal ManualStoreProductInput? Input { get; private set; }

    internal ManualStoreProductDialog(string supplierName, string originalUrl, Product? existing,
        MegaCoffeeProductSnapshot? lookup, string? failure)
    {
        Text = existing == null ? supplierName + " 상품 등록" : supplierName + " 상품 정보 수정";
        StartPosition = FormStartPosition.CenterParent; Size = new Size(650, 540); MinimumSize = new Size(510, 430);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7,
            Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var state = new Label { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(590, 0),
            Text = lookup == null ? "조회 실패 · " + (failure ?? "상품 정보를 확인할 수 없습니다.") + "\n상품명·가격·사진은 비워두고 등록한 뒤 수정할 수도 있습니다."
                : "실제 페이지 조회 결과 · 옵션별 가격은 판매처에서 확인하세요." };
        layout.Controls.Add(state, 0, 0); layout.SetColumnSpan(state, 2);
        AddRow(layout, 1, "상품 URL", new TextBox { Text = originalUrl, ReadOnly = true, Dock = DockStyle.Fill });
        AddRow(layout, 2, "상품명", name);
        AddRow(layout, 3, "표시 가격", price);
        var imageRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        imageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        imageRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        imageRow.Controls.Add(imagePath, 0, 0);
        var browse = new Button { Text = "사진 선택", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "이미지|*.jpg;*.jpeg;*.png;*.webp;*.bmp" };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try { SetPreview(ManualImages.Load(picker.FileName)); imagePath.Text = picker.FileName; droppedImage = null; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or ImageMagick.MagickException)
            { MessageBox.Show(this, "선택한 이미지를 읽지 못했습니다.", "사진", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        imageRow.Controls.Add(browse, 1, 0); AddRow(layout, 5, "사진", imageRow);
        layout.Controls.Add(preview, 0, 4); layout.SetColumnSpan(preview, 2);
        preview.AllowDrop = true;
        preview.DragEnter += (_, e) => HighlightDrop(e);
        preview.DragOver += (_, e) => HighlightDrop(e);
        preview.DragLeave += (_, _) => preview.BackColor = Color.FromArgb(232, 238, 230);
        preview.DragDrop += (_, e) =>
        {
            preview.BackColor = Color.FromArgb(232, 238, 230);
            if (!ImageDropData.CanAccept(e.Data)) return;
            try
            {
                byte[] bytes = ImageDropData.Read(e.Data);
                SetPreview(ManualImages.Load(bytes));
                droppedImage = bytes; imagePath.Text = "드래그한 이미지";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or ImageMagick.MagickException)
            { MessageBox.Show(this, "이미지를 읽지 못했습니다. JPG·PNG·WebP·BMP 파일을 확인해주세요.", "사진", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var save = new Button { Text = existing == null ? "등록" : "저장", AutoSize = true };
        bool unverifiedLegacy = lookup == null && existing is
            { LastSuccessfulCheckAtUtc: null, ImageCachePath: null, ManualImagePath: null } &&
            (existing.Id == 40 && existing.Supplier.Id == "naver" &&
             existing.Name == "국내생산 98mm(16/20/24온스 겸용) 아이스컵 십자뚜껑 100개" && existing.Price == 3900 ||
             existing.Id == 41 && existing.Supplier.Id == "coupang" &&
             existing.Name == "아이스컵 20온스 1,000개" && existing.Price == 67000);
        save.Click += (_, _) =>
        {
            string value = name.Text.Trim();
            bool hasPrice = price.Text.Trim().Length != 0;
            if (hasPrice && (!decimal.TryParse(price.Text.Trim(), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal numericPrice) || numericPrice is < 0 or > 999_999_999))
            {
                MessageBox.Show(this, "가격은 비워두거나 0 이상의 숫자를 입력해주세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
            }
            Input = new(value, hasPrice ? decimal.Parse(price.Text.Trim(), NumberStyles.Number,
                CultureInfo.InvariantCulture) : null,
                droppedImage == null && imagePath.Text.Length != 0 ? imagePath.Text : null, droppedImage);
            DialogResult = DialogResult.OK; Close();
        };
        var cancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 6);
        layout.SetColumnSpan(buttons, 2); Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        name.Text = lookup?.Name ?? (unverifiedLegacy ? "" : existing?.Name) ?? "";
        price.Text = lookup?.Price.ToString(CultureInfo.InvariantCulture) ??
            (unverifiedLegacy || existing?.PriceKnown == false ? "" : existing?.Price.ToString(CultureInfo.InvariantCulture)) ?? "";
        if (lookup != null)
        {
            try { using var stream = new MemoryStream(lookup.ImageBytes); using var loaded = Image.FromStream(stream); SetPreview(new Bitmap(loaded)); }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { }
        }
        else if (existing != null && !unverifiedLegacy)
        {
            var selected = ProductImages.Resolve(existing);
            if (selected.Owned) SetPreview(selected.Image);
        }
    }

    private static void AddRow(TableLayoutPanel layout, int row, string caption, Control field)
    { layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); layout.Controls.Add(field, 1, row); }
    private void HighlightDrop(DragEventArgs e)
    {
        bool ready = ImageDropData.CanAccept(e.Data);
        e.Effect = ready ? DragDropEffects.Copy : DragDropEffects.None;
        preview.BackColor = ready ? Color.FromArgb(197, 225, 198) : Color.FromArgb(232, 238, 230);
    }
    private void SetPreview(Image image) { ownedPreview?.Dispose(); ownedPreview = image; preview.Image = image; }
    protected override void Dispose(bool disposing) { if (disposing) ownedPreview?.Dispose(); base.Dispose(disposing); }
}
