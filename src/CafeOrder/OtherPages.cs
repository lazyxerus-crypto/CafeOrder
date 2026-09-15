namespace CafeOrder;

internal static class OtherPages
{
    public static Control History()
    {
        var grid = new DataGridView { Name = "HistoryGrid", Dock = DockStyle.Fill, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            ColumnHeadersVisible = true, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            EnableHeadersVisualStyles = false, RowHeadersVisible = false, BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 234, 226);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", 12, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8);
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.DefaultCellStyle.Padding = new Padding(8);
        foreach (string title in new[] { "주문일시", "판매처", "상품", "결제금액", "주문번호", "상태" }) grid.Columns.Add(title, title);
        grid.Columns[2].FillWeight = 250;
        grid.Rows.Add("09/15 14:20", "메가커피", "포모나 코코렛 파우더 800g 2개세트\n카페 블렌드 원두 1kg", "53,000원", "SAMPLE-001", "주문완료");
        grid.Rows.Add("09/14 10:35", "네이버 스마트스토어", "아이스컵 십자뚜껑 100개", "미입력", "미입력", "주문완료");
        grid.Rows.Add("09/14 09:10", "늘담", "비건 초콜릿 쿠키 24개입", "미확정", "미확인", "확인필요");
        return grid;
    }
    public static Control Suppliers(SampleData data)
    {
        var list = Ui.List("Suppliers"); list.SuspendLayout();
        foreach (var s in data.Suppliers)
        {
            var card = Ui.Column(Ui.Text(s.Name, true));
            if (s.Manual) ProductsView.AddRow(card, Ui.Text("로그인과 주문은 사이트에서 직접 진행합니다."));
            else
            {
                var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                var id = new TextBox { Name = $"LoginId_{s.Id}", Dock = DockStyle.Top, PlaceholderText = "아이디", MaxLength = 100 };
                var password = new TextBox { Name = $"LoginPassword_{s.Id}", Dock = DockStyle.Top, UseSystemPasswordChar = true, MaxLength = 100 };
                fields.Controls.Add(Ui.Column(Ui.Text("아이디"), id), 0, 0);
                fields.Controls.Add(Ui.Column(Ui.Text("비밀번호"), password), 1, 0);
                ProductsView.AddRow(card, fields);
                if (s.SampleNaverLogin)
                {
                    var linked = new CheckBox { Name = $"NaverLogin_{s.Id}", Text = "네이버 연동 로그인 (예시)", AutoSize = true, Margin = new Padding(8) };
                    linked.CheckedChanged += (_, _) => { id.Enabled = password.Enabled = !linked.Checked; };
                    ProductsView.AddRow(card, linked);
                }
                ProductsView.AddRow(card, Ui.Text("로그인 상태: 확인 안 됨"));
                var threshold = new DigitsTextBox { Name = $"Shipping_{s.Id}", Text = data.Shipping[s.Id].ToString("0"), Width = 180 };
                threshold.TextChanged += (_, _) => { if (decimal.TryParse(threshold.Text, out var value)) { data.Shipping[s.Id] = value; data.Notify(); } };
                ProductsView.AddRow(card, Ui.Row(Ui.Text("무료배송 기준"), threshold, Ui.Text("원  ·  0원은 기본 무료배송")));
                var payment = Ui.Combo(["선택 안 함", "신용/체크카드", "계좌이체"], $"Payment_{s.Id}");
                ProductsView.AddRow(card, Ui.Text("선호 결제수단")); ProductsView.AddRow(card, payment);
            }
            list.Controls.Add(card);
        }
        list.ResumeLayout(true); return list;
    }
    public static Control AddProduct(SampleData data)
    {
        var list = Ui.List("AddProduct");
        var url = new TextBox { Name = "ProductUrl", Dock = DockStyle.Top, PlaceholderText = "상품 URL을 입력하세요", Margin = new Padding(4) };
        var detected = Ui.Text("지원하지 않는 주소"); detected.Name = "DetectedSupplier";
        var category = Ui.Combo(SampleData.Categories.Skip(1), "AddCategory"); category.SelectedItem = "파우더";
        var preview = new Panel { Name = "ProductPreview", Height = 1, Visible = false, BackColor = Ui.Background };
        ProductCard? resultCard = null;
        void LayoutResult()
        {
            if (resultCard == null || resultCard.IsDisposed) return;
            int width = Math.Min(260, Math.Max(160, preview.ClientSize.Width));
            resultCard.SetBounds(0, 0, width, resultCard.GetPreferredSize(new Size(width, 0)).Height);
            preview.Height = resultCard.Height;
        }
        preview.SizeChanged += (_, _) => LayoutResult();
        var add = Ui.Button("상품 추가", () =>
        {
            var supplier = data.DetectSupplier(url.Text); if (supplier == null) return;
            var original = data.Products.First(p => p.Supplier.Id == supplier.Id);
            var sample = original with { Category = category.Text };
            if (resultCard?.Product == sample) { preview.Visible = true; return; }
            Ui.Clear(preview);
            var card = new ProductCard(sample, data.AddToCart);
            resultCard = card;
            preview.Controls.Add(card);
            LayoutResult(); preview.Visible = true;
        }, true, "AddProductButton");
        add.Enabled = false;
        url.TextChanged += (_, _) => { var supplier = data.DetectSupplier(url.Text); detected.Text = supplier?.Name ?? "지원하지 않는 주소"; add.Enabled = supplier != null; };
        list.Controls.Add(Ui.Column(Ui.Text("상품 URL", true), url, Ui.Text("공급처"), detected, Ui.Text("카테고리"), category, add));
        list.Controls.Add(preview); return list;
    }
    public static Control Logs() => new RichTextBox
    {
        Name = "LogText", Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = Color.White, ForeColor = Ui.Ink, DetectUrls = false, ShortcutsEnabled = true,
        Text = "02:31  메가커피 로그인 상태 확인 (예시)\n02:32  상품 데이터 확인 완료 (예시)\n02:34  장바구니에 상품 추가 (예시)"
    };
    public static Control Settings()
    {
        var list = Ui.List("Settings");
        list.Controls.Add(Ui.Column(Ui.Text("백업", true), Ui.Text("자동 백업 기본 기준: 하루 1회\n프로그램 업데이트 전 · 데이터 형식 변경 전 · 데이터 가져오기 전"),
            Ui.Text("무료배송 기준은 사이트관리에서 확인할 수 있습니다.")));
        return list;
    }
}

internal sealed class DigitsTextBox : TextBox
{
    public DigitsTextBox() { MaxLength = 9; Margin = new Padding(4, 6, 4, 6); }
    protected override void OnTextChanged(EventArgs e)
    {
        string filtered = new(Text.Where(char.IsAsciiDigit).ToArray());
        if (Text != filtered) { int start = SelectionStart; Text = filtered; SelectionStart = Math.Min(start, Text.Length); return; }
        base.OnTextChanged(e);
    }
}
