namespace CafeOrder;

public sealed class OrderForm : Form
{
    private sealed class OrderCard(Supplier supplier, string state)
    {
        public Supplier Supplier { get; } = supplier;
        public List<CartLine> Lines { get; set; } = [];
        public string State { get; set; } = state;
        public string Reason { get; set; } = "";
        public SiteCartAttempt? Attempt { get; set; }
        public bool BrowserOpened { get; set; }
        public bool Opened { get; set; }
        public bool Opening { get; set; }
        public TableLayoutPanel View { get; set; } = null!;
        public Label Status { get; set; } = null!;
        public Label Detail { get; set; } = null!;
        public Label Total { get; set; } = null!;
        public Button Action { get; set; } = null!;
        public Button Secondary { get; set; } = null!;
        public Dictionary<int, CartProductRow> Rows { get; } = [];
        public TableLayoutPanel Items { get; set; } = null!;
    }

    private readonly List<OrderCard> cards = [];
    private readonly HashSet<CartLine> targets;
    private readonly FlowLayoutPanel list = Ui.List("OrderCards");
    private readonly SampleData data;
    private readonly Button startAll;
    private readonly Button next;
    private readonly Label progress;
    private bool batch, closing;
    private int activeSitePreparations;
    private SupplierSessionManager? sessions;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal SupplierSessionManager? Sessions
    {
        get => sessions;
        set
        {
            sessions = value;
            if (value != null)
            {
                foreach (var card in cards.Where(card => !card.Supplier.Manual && Supported(card)))
                    Restore(card);
                UpdateActions();
            }
        }
    }

    public OrderForm(SampleData data, IEnumerable<CartLine> lines, bool startManual = false)
    {
        this.data = data; targets = lines.ToHashSet(); Text = "주문 진행"; Font = new Font("Malgun Gothic", 12);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 600); MinimumSize = new Size(620, 440); StartPosition = FormStartPosition.CenterParent;

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 3, RowCount = 1,
            Padding = new Padding(8, 4, 8, 4) };
        for (int column = 0; column < 3; column++)
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var close = Ui.Button("닫기", Close, name: "CloseOrders");
        next = Ui.Button("다음 판매처 열기", () => _ = OpenNextAsync(), name: "NextOrderSite");
        startAll = Ui.Button("전체 주문 시작", () => _ = StartAllAsync(), true, "StartAllOrders");
        void FitFooter()
        {
            int width = Math.Max(100, (ClientSize.Width - footer.Padding.Horizontal) / 3 - 8);
            footer.Height = new[] { close, next, startAll }.Max(b => Ui.ActionHeight(b, width)) +
                footer.Padding.Vertical + 8;
        }
        foreach (var button in new[] { close, next, startAll })
        { button.AutoSize = false; button.AutoEllipsis = true; button.Dock = DockStyle.Fill;
            button.FontChanged += (_, _) => FitFooter(); }
        FitFooter(); ClientSizeChanged += (_, _) => FitFooter();
        footer.Controls.Add(startAll, 0, 0); footer.Controls.Add(next, 1, 0);
        footer.Controls.Add(close, 2, 0);

        progress = Ui.Role(new Label { Name = "OrderProgress", AutoSize = true, Dock = DockStyle.Top,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 8),
            ForeColor = Ui.Ink, BackColor = Color.White }, TypographyKey.OrderInfo);
        var top = new Panel { Dock = DockStyle.Top, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Padding.Empty };
        top.Controls.Add(progress);
        void FitProgress() => progress.MaximumSize = new Size(Math.Max(100, ClientSize.Width - 20), 0);
        ClientSizeChanged += (_, _) => FitProgress(); FitProgress();
        Controls.Add(list); Controls.Add(footer); Controls.Add(top);

        foreach (var group in targets.GroupBy(x => x.Product.Supplier))
        {
            if (group.Key.Manual)
                foreach (var url in group.GroupBy(x => x.Product.Url))
                    AddCard(group.Key, url.ToList(), "PENDING");
            else AddCard(group.Key, group.ToList(), group.Key.Id is "mega" or "piece" or "nuldam"
                ? "PENDING" : "NOT_CONFIGURED");
        }
        data.CartChanged += Sync; Sync();
        if (startManual) Shown += (_, _) =>
        { if (cards.FirstOrDefault() is { } card && card.Supplier.Manual) StartOne(card); };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(620, area.Width), Math.Min(440, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width),
            Math.Clamp(Top, area.Top, area.Bottom - Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !closing) { closing = true; data.CartChanged -= Sync; data.Unlock(targets); }
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (activeSitePreparations > 0)
        {
            e.Cancel = true;
            MessageBox.Show(this, "사이트 장바구니 확인이 끝난 뒤 창을 닫을 수 있습니다.",
                "주문 진행", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        base.OnFormClosing(e);
    }

    private static bool Supported(OrderCard card) => card.Supplier.Id is "mega" or "piece" or "nuldam";

    private void AddCard(Supplier supplier, List<CartLine> lines, string state)
    {
        var card = new OrderCard(supplier, state) { Lines = lines };
        card.Status = Ui.Role(Ui.Text(""), TypographyKey.OrderInfo);
        card.Status.Name = $"OrderStatus_{lines[0].Product.Id}";
        card.Detail = Ui.Role(new Label { AutoSize = true, Dock = DockStyle.Top,
            Margin = new Padding(4, 0, 4, 5), ForeColor = Ui.Ink }, TypographyKey.OrderInfo);
        card.Items = Ui.Column(); card.Items.Padding = Padding.Empty;
        card.Total = Ui.Role(Ui.Text("", true), TypographyKey.OrderInfo);
        card.Action = Ui.Button("주문 시작", () => StartOne(card), true,
            $"OrderAction_{lines[0].Product.Id}");
        card.Secondary = Ui.Button("새 주문 준비", () =>
        {
            if (card.State == "WAITING_FOR_USER") _ = RecheckAsync(card, false);
            else if (CanStartNew(card) && !batch) _ = PrepareOneAsync(card);
        },
            name: $"OrderSite_{lines[0].Product.Id}");
        foreach (var button in new[] { card.Action, card.Secondary })
        { button.AutoSize = false; button.AutoEllipsis = true;
            button.Size = new Size(205, Ui.ActionHeight(button, 205)); }
        var actions = Ui.Row(card.Action, card.Secondary);
        card.View = Ui.Column(Ui.SellerHeading(supplier), card.Status, card.Detail,
            card.Items, card.Total, actions);
        card.View.Name = $"Order_{supplier.Id}_{lines[0].Product.Id}";
        card.View.SizeChanged += (_, _) => card.Detail.MaximumSize = new Size(
            Math.Max(80, card.View.ClientSize.Width - card.View.Padding.Horizontal - 8), 0);
        cards.Add(card); list.Controls.Add(card.View);
    }

    private void Sync()
    {
        if (closing) return;
        foreach (var card in cards.ToArray())
        {
            card.Lines.RemoveAll(line => !data.Cart.Contains(line));
            if (card.Lines.Count == 0) { cards.Remove(card); card.View.Dispose(); continue; }
            bool structure = !card.Rows.Keys.ToHashSet().SetEquals(card.Lines.Select(x => x.Product.Id));
            if (structure) card.Items.SuspendLayout();
            foreach (int id in card.Rows.Keys.Except(card.Lines.Select(x => x.Product.Id)).ToArray())
            { card.Rows[id].Dispose(); card.Rows.Remove(id); }
            foreach (var line in card.Lines)
            {
                if (!card.Rows.TryGetValue(line.Product.Id, out var row))
                {
                    row = new CartProductRow(data, line, true) { Dock = DockStyle.Top };
                    card.Rows[line.Product.Id] = row; ProductsView.AddRow(card.Items, row);
                    row.SizeChanged += (_, _) =>
                    { int height = row.GetPreferredSize(new Size(row.Width, 0)).Height;
                        if (row.Height != height) row.Height = height; };
                    row.Height = row.GetPreferredSize(new Size(400, 0)).Height;
                }
                row.RefreshQuantity();
            }
            if (structure) card.Items.ResumeLayout(true);
            string total = card.Supplier.Manual ? "수량/옵션·가격: 판매처에서 확인" :
                data.HasUnknownPrice(card.Lines) ? "합계 금액 미확정" : $"합계 {data.Subtotal(card.Lines):N0}원";
            if (card.Total.Text != total) card.Total.Text = total;
        }
        UpdateActions();
    }

    private bool Eligible(OrderCard card) => card.State == "PENDING" && CanStartNew(card);

    private bool CanStartNew(OrderCard card) => !card.Opening && card.State != "PREPARING" &&
        (card.Supplier.Manual || sessions != null && Supported(card)) &&
        data.Shortfall(card.Supplier, card.Lines) == 0 && card.Lines.All(data.CanStartLocalOrder) &&
        (card.Supplier.Manual || ValidTargets(card));

    private static bool ValidTargets(OrderCard card)
    {
        try { return BuildTargets(card).Count > 0; }
        catch (InvalidDataException) { return false; }
    }

    private static string Explain(string reason) => reason switch
    {
        "" => "", "BROWSER_OPENED" => "사이트 창을 연 이력이 있어 주문 여부를 직접 확인해 주세요.",
        "RECHECK_REQUIRED" => "이전 준비 결과가 저장돼 있습니다. 사이트 장바구니를 다시 확인해 주세요.",
        "INTERRUPTED" => "앱 종료 전에 준비가 끝나지 않았습니다. 사이트 상태를 확인해 주세요.",
        "LOGIN_REQUIRED" or "LOGIN_REQUIRED_BEFORE_MUTATION" => "판매처 로그인이 필요합니다.",
        "LOGIN_UNVERIFIED" => "로그인 상태를 확인하지 못했습니다.",
        "SITE_CART_ITEM_MISMATCH" or "SITE_CART_MISMATCH" =>
            "사이트 장바구니의 상품·옵션·수량이 주문 대상과 다릅니다.",
        "READ_FAILED" => "사이트 장바구니를 읽지 못했습니다.",
        "RETRY_SAFE_AFTER_RECHECK" => "사이트 내용을 확인했습니다. 재시도할 수 있습니다.",
        "PRICE_CHANGED" => "사이트 가격이 저장 가격과 다릅니다.",
        "SOLD_OUT" => "사이트에서 품절 상태를 확인했습니다.",
        "INVALID_SNAPSHOT" => "저장된 주문 대상이 올바르지 않습니다.",
        "INVALID_GOODS_NO" or "INVALID_PRODUCT_NUMBER" => "사이트 상품 코드를 확인할 수 없습니다.",
        "OPTION_OR_PURCHASE_UNIT_REQUIRES_REVIEW" or "PURCHASE_UNIT_CHANGED" =>
            "사이트 옵션 또는 구매 단위를 직접 확인해 주세요.",
        "QUANTITY_LIMIT_REQUIRES_REVIEW" or "QUANTITY_LIMIT_OR_CONTROL_CHANGED" or
            "QUANTITY_SET_UNVERIFIED" => "사이트 주문 수량을 확인하지 못했습니다.",
        "CART_PRICE_CONFIGURATION_REQUIRED" => "사이트의 주문 가격 설정을 확인해 주세요.",
        "CART_CLEAR_UNVERIFIED" or "CART_DELETE_UNVERIFIED" or "CART_NOT_EMPTY" =>
            "사이트 장바구니가 비었는지 확인하지 못했습니다.",
        "CART_ADD_UNVERIFIED" or "FINAL_CART_MISMATCH" =>
            "사이트 장바구니 반영 결과를 확인하지 못했습니다.",
        "CART_CHANGED_BEFORE_ADD" or "CART_ROW_CHANGED" or "PRODUCT_CHANGED_BEFORE_ADD" =>
            "사이트 상품 또는 장바구니 내용이 준비 중 바뀌었습니다.",
        "UNEXPECTED_CART_ADD_DIALOG" or "UNEXPECTED_DIALOG_AFTER_ADD" or
            "UNEXPECTED_DIALOG_BEFORE_FINAL_READ" => "예상하지 못한 사이트 확인창이 나타났습니다.",
        "CART_PAGE_UNAVAILABLE" => "사이트 장바구니 화면을 열지 못했습니다.",
        "CART_CHANGED" => "주문 대상 상품 또는 수량이 바뀌었습니다. 주문창을 다시 열어 주세요.",
        "INVALID_PRODUCT_URL" => "상품 링크를 확인할 수 없습니다.",
        "NOT_CONFIGURED" => "이 판매처의 사이트 장바구니 연결은 아직 없습니다.",
        _ when reason.StartsWith("READ_FAILED_", StringComparison.Ordinal) =>
            "사이트 장바구니를 읽지 못했습니다. 로그에서 오류 종류를 확인해 주세요.",
        _ when reason.StartsWith("BROWSER_OPEN_FAILED_", StringComparison.Ordinal) =>
            "판매처 Edge 창을 열지 못했습니다. 로그에서 오류 종류를 확인해 주세요.",
        _ => "준비를 마치지 못했습니다. 로그에서 원인을 확인해 주세요. (" + reason + ")"
    };

    private void UpdateActions()
    {
        if (closing || IsDisposed) return;
        foreach (var card in cards)
        {
            decimal shortage = data.Shortfall(card.Supplier, card.Lines);
            bool unknownPrice = data.HasUnknownPrice(card.Lines);
            bool soldOut = card.Lines.Any(line => !line.Product.Available);
            bool checking = card.Lines.Any(line => data.IsMegaCartLookupPending(line.Product));
            bool failedLookup = card.Lines.Any(line => data.MegaCartLookupFailed(line.Product));
            card.Status.Text = card.State switch
            {
                "PREPARING" => "사이트 장바구니 준비 중",
                "WAITING_FOR_USER" when card.Supplier.Manual => "판매처에서 직접 주문 확인 필요",
                "WAITING_FOR_USER" => "판매처 로그인·사용자 확인 필요",
                "READY" => card.Opened ? "✓ 사이트 장바구니 열림 · 주문/결제 미실행" :
                    "✓ 사이트 장바구니 준비 완료 · 주문/결제 미실행",
                "FAILED" => "사이트 장바구니 준비 실패",
                "UNKNOWN" => "사이트 장바구니 상태 확인 필요",
                "NOT_CONFIGURED" => "사이트 장바구니 연동 전",
                _ when unknownPrice => "가격 미입력 · 금액 확인 필요",
                _ when soldOut => "품절 상품 포함", _ when checking => "가격 확인 중",
                _ when failedLookup => "가격 확인 필요", _ => "주문 대기"
            };
            card.Detail.Text = card.Supplier.Manual && card.Opened
                ? "상품 링크를 기본 브라우저에서 열었습니다. 주문/결제 여부는 직접 확인해 주세요."
                : card.State == "NOT_CONFIGURED" ? Explain("NOT_CONFIGURED") : Explain(card.Reason);
            card.Status.ForeColor = card.State switch
            {
                "FAILED" => Ui.Danger, "UNKNOWN" => Color.FromArgb(156, 87, 24),
                "READY" => Ui.Accent, "PREPARING" => Color.FromArgb(140, 91, 20),
                "WAITING_FOR_USER" => Color.FromArgb(33, 86, 150), _ => Ui.Ink
            };
            card.Detail.ForeColor = card.Status.ForeColor;
            card.View.BackColor = card.State == "FAILED" ? Color.FromArgb(255, 246, 244) : Color.White;
            card.Action.Text = card.State switch
            {
                "READY" => "장바구니 열기", "FAILED" => "재시도", "UNKNOWN" => "사이트 확인",
                "PREPARING" => "준비 중",
                "WAITING_FOR_USER" when card.Supplier.Manual => "사이트에서 주문하기",
                "WAITING_FOR_USER" => "브라우저 열기", "NOT_CONFIGURED" => "연동 전",
                _ when card.Supplier.Manual => "사이트에서 주문하기", _ => "주문 시작"
            };
            if (card.State == "PENDING")
                card.Action.Text = unknownPrice && !card.Supplier.Manual ? "가격 미입력" :
                    soldOut ? "품절 상품 포함" : checking ? "가격 확인 중" :
                    failedLookup ? "가격 확인 필요" : shortage > 0 ? $"{shortage:N0}원 부족" :
                    card.Reason == "INVALID_PRODUCT_URL" ? "상품 링크 확인 필요" : card.Action.Text;
            card.Action.BackColor = card.State == "FAILED" || shortage > 0 ? Ui.Danger : Ui.Accent;
            card.Action.Enabled = !card.Opening && (card.State switch
            {
                "PENDING" => Eligible(card), "READY" => sessions != null && !card.Supplier.Manual,
                "FAILED" => card.Supplier.Manual || sessions != null && Supported(card) && card.Attempt != null,
                "UNKNOWN" => sessions != null && Supported(card) && card.Attempt != null,
                "WAITING_FOR_USER" => card.Supplier.Manual || sessions != null && Supported(card),
                _ => false
            });
            card.Secondary.Visible = !card.Supplier.Manual && card.State is ("FAILED" or "UNKNOWN" or "WAITING_FOR_USER");
            card.Secondary.Text = card.State == "WAITING_FOR_USER" ? "사이트 확인" : "새 주문 준비";
            card.Secondary.Enabled = card.State == "WAITING_FOR_USER" ?
                !card.Opening && sessions != null && card.Attempt != null : CanStartNew(card);
        }
        startAll.Enabled = !batch && cards.Any(CanStartNew);
        bool nextAuto = cards.Any(card => !card.Supplier.Manual && card.State == "READY" &&
            !card.Opened && !card.Opening);
        bool nextManual = cards.Any(card => card.Supplier.Manual && !card.Opened && !card.Opening &&
            card.State is ("PENDING" or "WAITING_FOR_USER"));
        next.Text = nextAuto ? "다음 판매처 열기" : "다음 상품 열기";
        next.Enabled = nextAuto || nextManual;
        progress.Text = $"준비 중 {cards.Count(card => card.State == "PREPARING")}  ·  " +
            $"준비 완료 {cards.Count(card => card.State == "READY")}  ·  " +
            $"사이트 확인 {cards.Count(card => !card.Supplier.Manual && card.State is "FAILED" or "UNKNOWN")}  ·  " +
            $"로그인/사용자 확인 {cards.Count(card => !card.Supplier.Manual && card.State == "WAITING_FOR_USER")}  ·  " +
            $"수동 주문 대기 {cards.Count(card => card.Supplier.Manual && card.State == "WAITING_FOR_USER")}";
    }

    private void Restore(OrderCard card)
    {
        List<SiteCartTarget> currentTargets;
        try { currentTargets = BuildTargets(card); }
        catch (InvalidDataException)
        { card.State = "PENDING"; card.Reason = "INVALID_PRODUCT_URL"; return; }
        try
        {
            var stored = data.Store.Database.ReadLatestSiteCartAttempt(card.Supplier.Id, currentTargets);
            if (stored == null)
            {
                if (data.Store.Database.LatestSiteCartBrowserOpened(card.Supplier.Id))
                { card.BrowserOpened = true; card.State = "UNKNOWN"; card.Reason = "BROWSER_OPENED"; }
                return;
            }
            card.Attempt = stored.Attempt; card.BrowserOpened = stored.Reason == "BROWSER_OPENED";
            card.State = stored.State is "READY" or "PREPARING" ? "UNKNOWN" : stored.State;
            card.Reason = card.BrowserOpened ? "BROWSER_OPENED" : stored.State switch
            { "READY" => "RECHECK_REQUIRED", "PREPARING" => "INTERRUPTED", _ => stored.Reason ?? "" };
            if (card.State == "UNKNOWN" && stored.State is ("READY" or "PREPARING") &&
                !card.BrowserOpened)
                data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId, "UNKNOWN", card.Reason);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_RESTORE_FAILED",
                "저장된 판매처 장바구니 진행 상태를 복원하지 못했습니다.",
                supplier: card.Supplier.Id, result: "FAILED", error: ex);
            card.State = "UNKNOWN"; card.Reason = "READ_FAILED";
        }
    }

    private static List<SiteCartTarget> BuildTargets(OrderCard card)
    {
        var result = new List<SiteCartTarget>();
        foreach (var line in card.Lines)
        {
            Product product = line.Product;
            Uri? url; string id; string option = "";
            switch (card.Supplier.Id)
            {
                case "mega":
                    if (!MegaCoffeeProductLookup.TryProductUrl(product.Url, out url, out id))
                        throw new InvalidDataException("메가커피 상품 코드를 확인할 수 없습니다.");
                    break;
                case "piece":
                    if (!PieceCakeProductLookup.TryProductUrl(product.Url, out url, out id))
                        throw new InvalidDataException("파미유 상품 코드를 확인할 수 없습니다.");
                    option = PieceCakeSiteCart.PurchaseKey(product.Name) ??
                        PieceCakeSiteCart.PurchaseKey(product.PriceText) ??
                        throw new InvalidDataException("파미유 상품 구매 단위를 확인할 수 없습니다.");
                    break;
                case "nuldam":
                    if (!NuldamProductLookup.TryProductUrl(product.Url, out url, out id))
                        throw new InvalidDataException("널담 상품 코드를 확인할 수 없습니다.");
                    option = NuldamProductLookup.PackageKey(product.Name);
                    break;
                default: throw new InvalidDataException("사이트 장바구니가 연결되지 않았습니다.");
            }
            result.Add(new(product.Id, id, url!.ToString(), product.Name,
                line.Quantity, product.Price, option));
        }
        return result;
    }

    private void StartOne(OrderCard card)
    {
        if (card.Supplier.Manual) { OpenManual(card); return; }
        if (card.State == "READY") { _ = OpenCartAsync(card); return; }
        if (card.State == "FAILED") { _ = RecheckAsync(card, false); return; }
        if (card.State == "UNKNOWN") { _ = RecheckAsync(card, false); return; }
        if (card.State == "WAITING_FOR_USER") { _ = OpenCartAsync(card); return; }
        if (Eligible(card) && !batch) _ = PrepareOneAsync(card);
    }

    private void OpenManual(OrderCard card)
    {
        if (!card.Supplier.Manual || card.Opening || card.Lines.Count == 0) return;
        if (!SellerLinks.OpenManualProduct(card.Lines[0].Product))
        {
            card.State = "FAILED"; card.Reason = "INVALID_PRODUCT_URL";
            data.Store.Log.Write(LogLevel.WARN, "MANUAL_PRODUCT_OPEN_FAILED",
                "수동 판매처 상품 링크를 기본 브라우저에서 열지 못했습니다.",
                supplier: card.Supplier.Id, productId: card.Lines[0].Product.Id, result: "FAILED");
        }
        else
        {
            card.State = "WAITING_FOR_USER"; card.Opened = true;
            data.Store.Log.Write(LogLevel.INFO, "MANUAL_PRODUCT_OPENED",
                "수동 판매처 상품 링크를 기본 브라우저에서 열었습니다. 주문 완료는 확인하지 않았습니다.",
                supplier: card.Supplier.Id, productId: card.Lines[0].Product.Id, result: "WAITING_FOR_USER");
        }
        UpdateActions();
    }

    private SiteCartAttempt Snapshot(OrderCard card)
    {
        var attempt = data.Store.Database.CreateSiteCartAttempt(card.Supplier.Id, BuildTargets(card));
        card.Attempt = attempt; card.State = "PREPARING"; card.Reason = "";
        card.BrowserOpened = false; card.Opened = false;
        data.Lock(card.Lines);
        data.Store.Log.Write(LogLevel.INFO, "SITE_CART_SNAPSHOT_SAVED",
            "판매처 주문 대상과 수량을 SQLite에 저장했습니다.",
            supplier: card.Supplier.Id, result: "PREPARING");
        UpdateActions();
        return attempt;
    }

    private void SnapshotFailed(OrderCard card, Exception ex)
    {
        card.State = "FAILED"; card.Reason = "SNAPSHOT_" + ex.GetType().Name;
        data.Unlock(card.Lines);
        data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_SNAPSHOT_FAILED",
            "판매처 주문 대상을 저장하지 못해 사이트 장바구니를 변경하지 않았습니다.",
            supplier: card.Supplier.Id, result: "FAILED", error: ex);
        UpdateActions();
    }

    private async Task PrepareOneAsync(OrderCard card)
    {
        try { await PrepareSavedAsync(card, Snapshot(card)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { SnapshotFailed(card, ex); }
    }

    private async Task StartAllAsync()
    {
        if (batch) return;
        batch = true;
        var pending = cards.Where(CanStartNew).ToArray();
        var saved = new List<(OrderCard Card, SiteCartAttempt Attempt)>();
        // Store every AUTO supplier snapshot before the first remote cart write.
        foreach (var card in pending.Where(card => !card.Supplier.Manual))
        {
            try { saved.Add((card, Snapshot(card))); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { SnapshotFailed(card, ex); }
        }
        foreach (var card in pending.Where(card => card.Supplier.Manual))
        { card.State = "WAITING_FOR_USER"; card.Reason = ""; }
        UpdateActions();
        await Task.WhenAll(saved.Select(pair => PrepareSavedAsync(pair.Card, pair.Attempt)));
        batch = false; UpdateActions();
        // Open only the first READY AUTO cart, and never open another window if the user
        // already opened one while this batch was preparing.
        if (!cards.Any(card => card.Opened) && cards.FirstOrDefault(card =>
            !card.Supplier.Manual && card.State == "READY") is { } firstReady)
        {
            list.ScrollControlIntoView(firstReady.View);
            await OpenCartAsync(firstReady);
        }
    }

    private async Task PrepareSavedAsync(OrderCard card, SiteCartAttempt attempt)
    {
        activeSitePreparations++;
        try
        {
            await sessions!.CloseCheckoutForNewOrderAsync(card.Supplier.Id);
            SiteCartPreparationResult result = card.Supplier.Id switch
            {
                "mega" => await sessions!.PrepareMegaSiteCartAsync(attempt, data.Store.Database,
                    _ => Task.FromResult(true)),
                "piece" => await sessions!.PreparePieceSiteCartAsync(attempt, data.Store.Database),
                "nuldam" => await sessions!.PrepareNuldamSiteCartAsync(attempt, data.Store.Database),
                _ => throw new InvalidDataException("사이트 장바구니가 연결되지 않았습니다.")
            };
            if (closing || IsDisposed) return;
            card.State = result.State; card.Reason = result.Reason;
            if (card.State != "READY") data.Unlock(card.Lines);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_PREPARE_FAILED",
                "판매처 사이트 장바구니 준비를 완료하지 못했습니다.",
                supplier: card.Supplier.Id, result: "UNKNOWN", error: ex);
            if (!closing && !IsDisposed)
            {
                card.State = "UNKNOWN"; card.Reason = "PREPARE_" + ex.GetType().Name;
                try { data.Store.Database.SetSiteCartAttemptState(attempt.AttemptId, "UNKNOWN", card.Reason); }
                catch (Exception dbEx) when (dbEx is not OutOfMemoryException)
                { data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_STATE_SAVE_FAILED",
                    "판매처 장바구니 불명확 상태를 저장하지 못했습니다.",
                    supplier: card.Supplier.Id, result: "FAILED", error: dbEx); }
                data.Unlock(card.Lines);
            }
        }
        finally { activeSitePreparations--; UpdateActions(); }
    }

    private async Task RecheckAsync(OrderCard card, bool retryAfterCheck)
    {
        if (sessions == null || card.Opening) return;
        if (card.Attempt == null)
        {
            if (retryAfterCheck && !card.BrowserOpened) await PrepareOneAsync(card);
            return;
        }
        card.Opening = true; UpdateActions();
        try
        {
            var current = BuildTargets(card);
            if (!SameTargets(card.Supplier.Id, card.Attempt.Targets, current))
            { card.State = "UNKNOWN"; card.Reason = "CART_CHANGED";
                data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId, card.State, card.Reason); return; }
            var check = await sessions.VerifySiteCartAsync(card.Supplier.Id, current);
            if (closing || IsDisposed) return;
            if (check.State == "READY")
            {
                card.State = "READY"; card.Reason = check.Reason;
                data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId, "READY", check.Reason);
                data.Lock(card.Lines);
            }
            else if (check.State == "WAITING_FOR_USER")
            {
                card.State = "WAITING_FOR_USER"; card.Reason = check.Reason;
                data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId, card.State, card.Reason);
            }
            else if (check.Reason == "SITE_CART_MISMATCH")
            {
                card.State = "FAILED"; card.Reason = "SITE_CART_MISMATCH";
                data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId, card.State, card.Reason);
            }
            else
            {
                card.State = "UNKNOWN"; card.Reason = check.Reason;
                data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId, card.State, card.Reason);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            card.State = "UNKNOWN"; card.Reason = "READ_FAILED_" + ex.GetType().Name;
            data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_RECHECK_FAILED",
                "판매처 장바구니를 읽기 전용으로 확인하지 못했습니다.",
                supplier: card.Supplier.Id, result: "UNKNOWN", error: ex);
        }
        finally { card.Opening = false; UpdateActions(); }
    }

    private async Task OpenCartAsync(OrderCard card)
    {
        if (sessions == null || !Supported(card) || card.Opening) return;
        card.Opening = true; UpdateActions();
        try
        {
            var opened = await sessions.OpenSiteCartForUserAsync(card.Supplier.Id);
            if (closing || IsDisposed) return;
            if (opened.State == "OPENED")
            {
                card.Opened = true; card.BrowserOpened = true;
                if (card.Attempt != null) data.Store.Database.MarkSiteCartBrowserOpened(card.Attempt.AttemptId);
            }
            else if (opened.State == "WAITING_FOR_USER")
            { card.State = "WAITING_FOR_USER"; card.Reason = opened.Reason; }
            else { card.State = "UNKNOWN"; card.Reason = opened.Reason; }
            if (card.Attempt != null && opened.State != "OPENED")
                data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId, card.State, card.Reason);
            data.Store.Log.Write(opened.State == "OPENED" ? LogLevel.INFO : LogLevel.WARN,
                "SITE_CART_WINDOW", opened.State == "OPENED" ?
                    "판매처 장바구니를 Edge에서 열었습니다. 주문·결제는 실행하지 않았습니다." :
                    "판매처 장바구니 창을 열었으나 추가 확인이 필요합니다.",
                supplier: card.Supplier.Id, result: opened.State, reason: opened.Reason);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            card.State = "UNKNOWN"; card.Reason = "BROWSER_OPEN_FAILED_" + ex.GetType().Name;
            if (card.Attempt != null)
                try { data.Store.Database.SetSiteCartAttemptState(card.Attempt.AttemptId,
                    "UNKNOWN", card.Reason); }
                catch (Exception saveError) when (saveError is not OutOfMemoryException)
                { data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_STATE_SAVE_FAILED",
                    "브라우저 열기 실패 상태를 SQLite에 저장하지 못했습니다.",
                    supplier: card.Supplier.Id, result: "FAILED", error: saveError); }
            data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_WINDOW_FAILED",
                "판매처 장바구니 Edge 창을 열지 못했습니다.",
                supplier: card.Supplier.Id, result: "UNKNOWN", error: ex);
        }
        finally { card.Opening = false; UpdateActions(); }
    }

    private async Task OpenNextAsync()
    {
        if (closing || IsDisposed) return;
        var card = cards.FirstOrDefault(c => !c.Supplier.Manual && c.State == "READY" &&
            !c.Opened && !c.Opening) ?? cards.FirstOrDefault(c => c.Supplier.Manual &&
            c.State is ("PENDING" or "WAITING_FOR_USER") && !c.Opened && !c.Opening);
        if (card == null) return;
        list.ScrollControlIntoView(card.View);
        if (card.Supplier.Manual) OpenManual(card);
        else await OpenCartAsync(card);
    }

    private static bool SameTargets(string supplierId, IReadOnlyList<SiteCartTarget> left,
        IReadOnlyList<SiteCartTarget> right)
    {
        if (left.Count != right.Count) return false;
        var byId = right.ToDictionary(item => item.ProductId);
        return left.All(item => byId.TryGetValue(item.ProductId, out var current) &&
            item.ExternalProductId == current.ExternalProductId && item.Quantity == current.Quantity &&
            item.Price == current.Price && item.OptionKey == current.OptionKey &&
            ProductUrlIdentity.Key(supplierId, item.ProductUrl) ==
            ProductUrlIdentity.Key(supplierId, current.ProductUrl));
    }
}
