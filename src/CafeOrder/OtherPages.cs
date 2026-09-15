namespace CafeOrder;

internal static class OtherPages
{
    public static Control History()
    {
        var grid = new DataGridView { Name = "HistoryGrid", Dock = DockStyle.Fill, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.DefaultCellStyle.Padding = new Padding(8);
        foreach (string title in new[] { "주문일시", "공급처", "주문 상품", "최종 금액", "주문번호", "상태" }) grid.Columns.Add(title, title);
        grid.Columns[2].FillWeight = 250;
        grid.Rows.Add("샘플 09/15 14:20", "메가커피", "포모나 코코렛 파우더 800g 2개세트\n카페 블렌드 원두 1kg", "53,000원", "SAMPLE-001", "COMPLETED");
        grid.Rows.Add("샘플 09/14 10:35", "네이버 스마트스토어", "아이스컵 십자뚜껑 100개", "미입력", "미입력", "COMPLETED");
        grid.Rows.Add("샘플 09/14 09:10", "늘담", "비건 초콜릿 쿠키 24개입", "미확정", "미확인", "UNKNOWN");
        return grid;
    }
    public static Control Suppliers(SampleData data)
    {
        var list = Ui.List("Suppliers");
        foreach (var s in data.Suppliers)
        {
            var card = Ui.Column(Ui.Text($"{s.Name} · {(s.Manual ? "MANUAL_BROWSER" : "AUTO")}", true));
            if (s.Manual) ProductsView.AddRow(card, Ui.Text("사용자가 사이트에서 직접 로그인 / 주문\n자동 로그인 설정 및 무료배송 조건 적용 없음"));
            else
            {
                ProductsView.AddRow(card, Ui.Text("로그인 상태: 미확인 (샘플) · 방식: 공급처별 브라우저 프로필\n마지막 로그인 확인: 없음 · Adapter version: 미구현"));
                var threshold = new NumericUpDown { Minimum = 0, Maximum = 10000000, Increment = 1000,
                    ThousandsSeparator = true, Value = data.Shipping[s.Id], Width = 180, Margin = new Padding(6) };
                threshold.ValueChanged += (_, _) => { data.Shipping[s.Id] = threshold.Value; data.Notify(); };
                ProductsView.AddRow(card, Ui.Row(Ui.Text("무료배송 기준 (원)"), threshold, Ui.Text("0원 = 기본 무료배송 · 임시 적용")));
                ProductsView.AddRow(card, Ui.Text("선호 결제수단: 미설정"));
            }
            list.Controls.Add(card);
        }
        return list;
    }
    public static Control AddProduct(SampleData data)
    {
        var list = Ui.List("AddProduct");
        var url = new TextBox { Name = "ProductUrl", Dock = DockStyle.Top, PlaceholderText = "상품 URL을 입력하세요", Margin = new Padding(4) };
        var supplier = Ui.Combo(data.Suppliers.Select(x => x.Name), "AddSupplier");
        var category = Ui.Combo(SampleData.Categories.Skip(1), "AddCategory");
        var preview = Ui.Column(); preview.Visible = false;
        var check = Ui.Button("상품 확인", () =>
        {
            if (!Uri.TryCreate(url.Text, UriKind.Absolute, out var parsed) || (parsed.Scheme != "https" && parsed.Scheme != "http"))
            { Ui.Notice(list, "http 또는 https 상품 URL을 입력해주세요."); return; }
            Ui.Clear(preview); preview.RowCount = 0; preview.RowStyles.Clear();
            ProductsView.AddRow(preview, Ui.Placeholder(category.Text));
            ProductsView.AddRow(preview, Ui.Text("샘플 미리보기 · 입력 URL에서 수집한 정보가 아닙니다.", true));
            ProductsView.AddRow(preview, Ui.Text($"{data.Products[0].Name}\n{data.Products[0].PriceText}\n공급처: {supplier.Text}\n카테고리: {category.Text}"));
            ProductsView.AddRow(preview, Ui.Button("등록", () => Ui.Notice(list, "목업 등록 확인 · 실제 저장은 하지 않습니다."), true, "RegisterPreview"));
            preview.Visible = true;
        }, true, "CheckProduct");
        list.Controls.Add(Ui.Column(Ui.Text("상품 URL", true), url, Ui.Text("공급처"), supplier, Ui.Text("카테고리"), category, check));
        list.Controls.Add(preview); return list;
    }
    public static Control Logs()
    {
        var list = Ui.List("Logs");
        list.Controls.Add(Ui.Column(Ui.Text("샘플 로그", true), Ui.Text("02:31  메가커피 로그인 상태 확인 (예시)\n02:32  상품 데이터 확인 완료 (예시)\n02:34  장바구니에 상품 추가 (예시)")));
        return list;
    }
    public static Control Settings()
    {
        var list = Ui.List("Settings");
        list.Controls.Add(Ui.Column(Ui.Text("백업", true), Ui.Text("자동 백업 기본 기준: 하루 1회\n프로그램 업데이트 전 · DB migration 전 · 데이터 import 전\n\n이 목업에서는 백업을 실행하거나 설정을 저장하지 않습니다."),
            Ui.Text("무료배송 기준은 사이트관리에서 확인할 수 있습니다.")));
        return list;
    }
}
