namespace CafeOrder;

internal static class OtherPages
{
    public static Control History(SampleData data)
    {
        var list = Ui.List("HistoryCards");
        AddHistory(data.Suppliers[0], "주문완료", "2026-09-15 14:20", "SAMPLE-001",
            [(data.Products[0], "14,500원", "2", "29,000원"), (data.Products[6], "24,000원", "1", "24,000원")], "53,000원");
        AddHistory(data.Suppliers[6], "주문완료", "2026-09-14 10:35", "미확인",
            [(data.Products[5], "미확인", "미확인", "미확인")], "미확인");
        AddHistory(data.Suppliers[4], "확인필요", "2026-09-14 09:10", "미확인",
            [(data.Products[21], "미확인", "미확인", "미확인")], "미확인");
        return list;
        void AddHistory(Supplier supplier, string state, string time, string number,
            (Product Product, string Price, string Qty, string Amount)[] items, string total)
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            header.Controls.Add(Ui.SellerHeading(supplier), 0, 0);
            var status = Ui.Text(state, true); status.TextAlign = ContentAlignment.MiddleRight;
            status.ForeColor = state == "주문완료" ? Ui.Accent : Ui.Danger; header.Controls.Add(status, 1, 0);
            var table = new HistoryItemsTable(data, items);
            var payment = Ui.Role(Ui.Text($"총 결제 {total}", true), TypographyKey.HistoryInfo); payment.TextAlign = ContentAlignment.MiddleRight;
            list.Controls.Add(Ui.Column(header, Ui.Role(Ui.Text($"주문일시  {time}\n주문번호  {number}"), TypographyKey.HistoryInfo), table, payment));
        }
    }
    public static Control Suppliers(SampleData data, SupplierSessionManager sessions, Control uiDispatcher)
    {
        var list = Ui.List("Suppliers"); list.SuspendLayout();
        foreach (var s in data.Suppliers)
        {
            var first = Ui.Row(Ui.SellerHeading(s));
            var id = new AsciiLoginTextBox { Name = $"LoginId_{s.Id}", PlaceholderText = "아이디" };
            var password = new AsciiLoginTextBox { Name = $"LoginPassword_{s.Id}", UseSystemPasswordChar = true };
            var caps = Ui.Text("⚠ Caps Lock 켜짐"); caps.Name = $"CapsLock_{s.Id}"; caps.ForeColor = Ui.Danger; caps.Visible = false;
            password.CapsLockChanged += active => caps.Visible = active;
            first.Controls.Add(Ui.Row(Ui.Text("아이디"), id)); first.Controls.Add(Ui.Row(Ui.Text("비밀번호"), password));
            first.Controls.Add(caps);
            if (s.SupportsNaverLogin)
            {
                var linked = new CheckBox { Name = $"NaverLogin_{s.Id}", Text = "네이버 연동 로그인", AutoSize = true, Margin = new Padding(8) };
                linked.CheckedChanged += (_, _) => { id.Enabled = password.Enabled = !linked.Checked; }; first.Controls.Add(linked);
            }
            var status = Ui.Text("로그인 상태: 확인 전"); status.Name = $"LoginStatus_{s.Id}";
            var login = Ui.Button("로그인", () =>
            {
                if (s.Manual || !sessions.IsConfigured(s.Id)) { status.Text = "로그인 상태: 연결 준비 중"; return; }
                var entered = id.Text.Length > 0 && password.Text.Length > 0 ? new LoginCredentials(id.Text, password.Text) : null;
                password.Clear();
                _ = sessions.OpenLoginAsync(s.Id, entered);
            }, name: $"Login_{s.Id}");
            void ShowLoginState(string supplierId, SupplierLoginState state)
            {
                if (supplierId != s.Id || status.IsDisposed) return;
                if (uiDispatcher.InvokeRequired)
                {
                    try { uiDispatcher.BeginInvoke(() => ShowLoginState(supplierId, state)); }
                    catch (InvalidOperationException) { }
                    return;
                }
                status.Text = "로그인 상태: " + (state switch
                {
                    SupplierLoginState.Checking => "확인 중",
                    SupplierLoginState.LoginRequired => "로그인 필요",
                    SupplierLoginState.LoggedIn => "로그인 완료",
                    SupplierLoginState.WaitingForUser => "사용자 확인 대기",
                    SupplierLoginState.Error => "확인 실패",
                    SupplierLoginState.NotConfigured => "연결 준비 중",
                    _ => "확인 전"
                });
                status.ForeColor = state == SupplierLoginState.LoggedIn ? Ui.Accent
                    : state == SupplierLoginState.Error ? Ui.Danger : Ui.Ink;
                login.Text = state == SupplierLoginState.LoggedIn ? "다시 로그인" : "로그인";
            }
            sessions.StateChanged += ShowLoginState;
            var second = Ui.Row(status, login);
            if (!s.Manual)
            {
                var threshold = new DigitsTextBox { Name = $"Shipping_{s.Id}", Text = data.Shipping[s.Id].ToString("0"), Width = 120 };
                threshold.TextChanged += (_, _) => { if (decimal.TryParse(threshold.Text, out var value)) { data.Shipping[s.Id] = value; data.Notify(); } };
                second.Controls.AddRange([Ui.Text("    무료배송 기준"), threshold, Ui.Text("원")]);
            }
            var card = new SupplierSettingsCard(first, second);
            card.Disposed += (_, _) => sessions.StateChanged -= ShowLoginState;
            caps.VisibleChanged += (_, _) => { if (card.Visible) card.Remeasure(); };
            list.Controls.Add(card);
        }
        list.ResumeLayout(true); return list;
    }
    public static Control Logs(SampleData data)
    {
        var log = new RichTextBox { Name = "LogText", Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = Ui.Ink, DetectUrls = false, ShortcutsEnabled = true,
            Text = "2026-09-15 02:31:00 [INFO] APP: UI 목업 시작\n2026-09-15 02:31:01 [INFO] PRODUCTS: 샘플 상품 24개 준비\n2026-09-15 02:32:00 [INFO] CART: 수량 변경 반영\n2026-09-15 02:33:00 [WARN] ORDER: 주문 결과 확인 필요 (예시)" };
        Ui.Role(log, TypographyKey.Log);
        var copy = Ui.Button("전체 복사", () => { }, name: "CopyAllLogs");
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(log.Text); copy.Text = "복사됨"; }
            catch (System.Runtime.InteropServices.ExternalException) { copy.Text = "복사 실패 · 다시 시도"; }
        };
        var toolbar = Ui.Row(copy); toolbar.Dock = DockStyle.Top;
        var panel = new SoftPanel { Dock = DockStyle.Fill }; panel.Controls.Add(log); panel.Controls.Add(toolbar); return panel;
    }
    public static Control Settings(SampleData data, Action openTypography)
    {
        var list = Ui.List("Settings");
        list.Controls.Add(Ui.Column(Ui.Button("글자 크기 상세 설정", openTypography, name: "OpenTypography")));
        list.Controls.Add(Ui.Column(Ui.Text("백업", true), Ui.Text("자동 백업 기본 기준: 하루 1회\n프로그램 업데이트 전 · 데이터 형식 변경 전 · 데이터 가져오기 전"),
            Ui.Text("무료배송 기준은 판매처관리에서 확인할 수 있습니다.")));
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
