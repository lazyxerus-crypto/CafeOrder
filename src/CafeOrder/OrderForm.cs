namespace CafeOrder;

public sealed class OrderForm : Form
{
    private sealed class OrderCard(Supplier supplier, string state)
    {
        public Supplier Supplier { get; } = supplier;
        public List<CartLine> Lines { get; set; } = [];
        public string State { get; set; } = state;
        public TableLayoutPanel View { get; set; } = null!;
        public Label Status { get; set; } = null!;
        public Label Total { get; set; } = null!;
        public Button Action { get; set; } = null!;
        public Dictionary<int, CartProductRow> Rows { get; } = [];
        public TableLayoutPanel Items { get; set; } = null!;
    }
    private readonly List<OrderCard> cards = [];
    private readonly HashSet<CartLine> targets;
    private readonly FlowLayoutPanel list = Ui.List("OrderCards");
    private readonly SampleData data;
    private readonly Button startAll;
    private bool busy, batch, closing;
    private bool activeSitePreparation;
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
                foreach (var card in cards.Where(card => !card.Supplier.Manual &&
                    card.Supplier.Id is not ("mega" or "piece")))
                    card.State = "NOT_CONFIGURED";
                UpdateActions();
            }
        }
    }

    public OrderForm(SampleData data, IEnumerable<CartLine> lines, bool startManual = false)
    {
        this.data = data; targets = lines.ToHashSet(); Text = "주문 진행"; Font = new Font("Malgun Gothic", 12);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 600); MinimumSize = new Size(620, 440); StartPosition = FormStartPosition.CenterParent;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft };
        var close = Ui.Button("닫기", Close, name: "CloseOrders");
        startAll = Ui.Button("전체 주문 시작", StartAll, true, "StartAllOrders");
        void FitFooter()
        {
            int width = Math.Max(170, new[] { close, startAll }.Max(b => TextRenderer.MeasureText(b.Text, b.Font).Width + b.Padding.Horizontal + 12));
            int height = new[] { close, startAll }.Max(b => Ui.ActionHeight(b, width));
            foreach (var button in new[] { close, startAll }) button.Size = new Size(width, height);
        }
        foreach (var button in new[] { close, startAll }) { button.AutoSize = false; button.FontChanged += (_, _) => FitFooter(); } FitFooter();
        footer.Controls.Add(startAll); footer.Controls.Add(close); Controls.Add(list); Controls.Add(footer);
        foreach (var group in targets.GroupBy(x => x.Product.Supplier))
        {
            if (group.Key.Manual)
                foreach (var url in group.GroupBy(x => x.Product.Url)) AddCard(group.Key, url.ToList(), "PENDING");
            else AddCard(group.Key, group.ToList(), group.Key.Id == "nuldam" ? "UNKNOWN" : "PENDING");
        }
        data.CartChanged += Sync; Sync();
        if (startManual) Shown += (_, _) => { if (cards.FirstOrDefault() is { } card) StartOne(card); };
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(620, area.Width), Math.Min(440, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !closing) { closing = true; data.CartChanged -= Sync; data.Unlock(targets); }
        base.Dispose(disposing);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (activeSitePreparation)
        {
            e.Cancel = true;
            MessageBox.Show(this, "사이트 장바구니 확인이 끝난 뒤 창을 닫을 수 있습니다.", "주문 진행",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        base.OnFormClosing(e);
    }
    private void AddCard(Supplier supplier, List<CartLine> lines, string state)
    {
        var card = new OrderCard(supplier, state) { Lines = lines };
        card.Status = Ui.Role(Ui.Text(""), TypographyKey.OrderInfo); card.Status.Name = $"OrderStatus_{lines[0].Product.Id}";
        card.Items = Ui.Column(); card.Items.Padding = Padding.Empty;
        card.Total = Ui.Role(Ui.Text("", true), TypographyKey.OrderInfo);
        card.Action = Ui.Button(supplier.Manual ? "판매처에서 주문하기" : "주문 시작", () => StartOne(card), true, $"OrderAction_{lines[0].Product.Id}");
        card.Action.AutoSize = false; card.Action.Size = new Size(220, Ui.ActionHeight(card.Action, 220));
        card.View = Ui.Column(Ui.SellerHeading(supplier), card.Status, card.Items, card.Total, card.Action);
        card.View.Name = $"Order_{supplier.Id}_{lines[0].Product.Id}"; cards.Add(card); list.Controls.Add(card.View);
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
            foreach (int id in card.Rows.Keys.Except(card.Lines.Select(x => x.Product.Id)).ToArray()) { card.Rows[id].Dispose(); card.Rows.Remove(id); }
            foreach (var line in card.Lines)
            {
                if (!card.Rows.TryGetValue(line.Product.Id, out var row))
                {
                    row = new CartProductRow(data, line, true) { Dock = DockStyle.Top }; card.Rows[line.Product.Id] = row;
                    ProductsView.AddRow(card.Items, row);
                    row.SizeChanged += (_, _) => { int height = row.GetPreferredSize(new Size(row.Width, 0)).Height; if (row.Height != height) row.Height = height; };
                    row.Height = row.GetPreferredSize(new Size(400, 0)).Height;
                }
                row.RefreshQuantity();
            }
            if (structure) card.Items.ResumeLayout(true);
            string total = card.Supplier.Manual ? "수량/옵션: 판매처에서 선택" : $"합계 {data.Subtotal(card.Lines):N0}원";
            if (card.Total.Text != total) card.Total.Text = total;
        }
        UpdateActions();
    }
    private bool Eligible(OrderCard card) => card.State == "PENDING" && data.Shortfall(card.Supplier, card.Lines) == 0 &&
        card.Lines.All(data.CanStartLocalOrder);
    private void UpdateActions()
    {
        foreach (var card in cards)
        {
            decimal shortage = data.Shortfall(card.Supplier, card.Lines);
            bool soldOut = card.Lines.Any(line => !line.Product.Available);
            bool checking = card.Lines.Any(line => data.IsMegaCartLookupPending(line.Product));
            bool failed = card.Lines.Any(line => data.MegaCartLookupFailed(line.Product));
            card.Status.Text = card.State switch
            {
                "PREPARING" => "사이트 장바구니 확인 중...", "WAITING_FOR_USER" => "사이트 장바구니 사용자 확인 필요",
                "READY" => "✓ 사이트 장바구니 준비 완료 · 주문/결제 미실행", "COMPLETED" => "✓ 주문 완료",
                "NOT_CONFIGURED" => "사이트 장바구니 연동 전",
                "FAILED" when Sessions != null && card.Supplier.Id is "mega" or "piece" => "사이트 장바구니 준비 실패",
                "UNKNOWN" when Sessions != null && card.Supplier.Id is "mega" or "piece" => "사이트 장바구니 상태 확인 필요",
                "FAILED" => "주문실패 · 주문 내용을 확인해주세요.", "UNKNOWN" => "확인필요 · 주문 여부를 확인할 수 없습니다.",
                _ when soldOut => "품절 상품 포함", _ when checking => "가격 확인 중", _ when failed => "가격 확인 필요", _ => "주문 대기"
            };
            card.Action.Text = soldOut ? "품절 상품 포함" : checking ? "가격 확인 중" : failed ? "가격 확인 필요" :
                shortage > 0 ? $"{shortage:N0}원 부족" : card.Supplier.Manual ? "판매처에서 주문하기" :
                card.State == "READY" ? "장바구니 준비 완료" : "주문 시작";
            card.Action.BackColor = shortage > 0 ? Ui.Danger : Ui.Accent; card.Action.Enabled = !busy && Eligible(card);
        }
        startAll.Enabled = !busy && !batch && cards.Any(Eligible);
    }
    private void StartOne(OrderCard card)
    {
        if (busy || !Eligible(card)) return;
        busy = true; data.Lock(card.Lines);
        if (card.Supplier.Id == "mega" && Sessions != null) _ = RunMegaAsync(card);
        else if (card.Supplier.Id == "piece" && Sessions != null) _ = RunPieceAsync(card);
        else RunCard(card);
    }
    private void StartAll()
    {
        if (busy || batch) return;
        batch = true; busy = true; data.Lock(targets.Where(data.Cart.Contains)); UpdateActions();
        BeginInvoke(Advance);
    }
    private void Advance()
    {
        if (closing || IsDisposed) return;
        var card = cards.FirstOrDefault(Eligible);
        if (card == null) { busy = false; batch = false; UpdateActions(); return; }
        if (card.Supplier.Manual) { busy = false; UpdateActions(); list.ScrollControlIntoView(card.View); return; }
        busy = true;
        if (card.Supplier.Id == "mega" && Sessions != null) _ = RunMegaAsync(card);
        else if (card.Supplier.Id == "piece" && Sessions != null) _ = RunPieceAsync(card);
        else RunCard(card);
    }
    private async Task RunMegaAsync(OrderCard card)
    {
        activeSitePreparation = true;
        card.State = "PREPARING"; UpdateActions();
        try
        {
            var targets = new List<SiteCartTarget>();
            foreach (var line in card.Lines)
            {
                if (!MegaCoffeeProductLookup.TryProductUrl(line.Product.Url, out var url, out var goodsNo))
                    throw new InvalidDataException("메가커피 상품 코드를 확인할 수 없습니다.");
                targets.Add(new(line.Product.Id, goodsNo, url!.ToString(), line.Product.Name,
                    line.Quantity, line.Product.Price));
            }
            var attempt = data.Store.Database.CreateSiteCartAttempt("mega", targets);
            data.Store.Log.Write(LogLevel.INFO, "SITE_CART_PREPARE_STARTED",
                "메가커피 주문 대상과 수량을 SQLite에 저장한 뒤 사이트 확인을 시작했습니다.",
                supplier: "mega", result: "STARTED");
            var result = await Sessions!.PrepareMegaSiteCartAsync(attempt, data.Store.Database,
                existing => ConfirmExistingAsync(existing));
            if (!closing && !IsDisposed)
            {
                card.State = result.State;
                if (result.State != "READY") data.Unlock(card.Lines);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_PREPARE_FAILED",
                "메가커피 사이트 장바구니 준비를 시작하거나 완료하지 못했습니다.",
                supplier: "mega", result: "FAILED", error: ex);
            if (!closing && !IsDisposed) { card.State = "FAILED"; data.Unlock(card.Lines); }
        }
        finally
        {
            activeSitePreparation = false;
            if (!closing && !IsDisposed)
            { busy = false; UpdateActions(); if (batch) BeginInvoke(Advance); }
        }
    }

    private async Task RunPieceAsync(OrderCard card)
    {
        activeSitePreparation = true;
        card.State = "PREPARING"; UpdateActions();
        try
        {
            var targets = new List<SiteCartTarget>();
            foreach (var line in card.Lines)
            {
                if (!PieceCakeProductLookup.TryProductUrl(line.Product.Url, out var url, out var number))
                    throw new InvalidDataException("파미유 상품 코드를 확인할 수 없습니다.");
                string? purchaseKey = PieceCakeSiteCart.PurchaseKey(line.Product.Name) ??
                    PieceCakeSiteCart.PurchaseKey(line.Product.PriceText);
                if (purchaseKey == null)
                    throw new InvalidDataException("파미유 상품 구매 단위를 확인할 수 없습니다.");
                targets.Add(new(line.Product.Id, number, url!.ToString(), line.Product.Name,
                    line.Quantity, line.Product.Price, purchaseKey));
            }
            var attempt = data.Store.Database.CreateSiteCartAttempt("piece", targets);
            data.Store.Log.Write(LogLevel.INFO, "SITE_CART_PREPARE_STARTED",
                "파미유 주문 대상과 수량을 SQLite에 저장한 뒤 사이트 확인을 시작했습니다.",
                supplier: "piece", result: "STARTED");
            var result = await Sessions!.PreparePieceSiteCartAsync(attempt, data.Store.Database);
            if (!closing && !IsDisposed)
            {
                card.State = result.State;
                if (result.State != "READY") data.Unlock(card.Lines);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            data.Store.Log.Write(LogLevel.ERROR, "SITE_CART_PREPARE_FAILED",
                "파미유 사이트 장바구니 준비를 시작하거나 완료하지 못했습니다.",
                supplier: "piece", result: "FAILED", error: ex);
            if (!closing && !IsDisposed) { card.State = "FAILED"; data.Unlock(card.Lines); }
        }
        finally
        {
            activeSitePreparation = false;
            if (!closing && !IsDisposed)
            { busy = false; UpdateActions(); if (batch) BeginInvoke(Advance); }
        }
    }

    private Task<bool> ConfirmExistingAsync(IReadOnlyList<SiteCartEntry> existing, string supplierName = "메가커피")
    {
        if (closing || IsDisposed) return Task.FromResult(false);
        if (InvokeRequired)
        {
            var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginInvoke(() =>
            {
                try { response.SetResult(ConfirmExisting(existing, supplierName)); }
                catch (Exception ex) { response.SetException(ex); }
            });
            return response.Task;
        }
        return Task.FromResult(ConfirmExisting(existing, supplierName));
    }

    private bool ConfirmExisting(IReadOnlyList<SiteCartEntry> existing, string supplierName)
    {
        if (closing || IsDisposed) return false;
        string current = string.Join(Environment.NewLine, existing.Take(8).Select(item =>
            $"• {item.Name} · {item.Quantity}개"));
        if (existing.Count > 8) current += $"{Environment.NewLine}외 {existing.Count - 8}건";
        return MessageBox.Show(this,
            $"{supplierName} 사이트 장바구니에 기존 상품 {existing.Count}건이 있습니다.{Environment.NewLine}" +
            $"{current}{Environment.NewLine}{Environment.NewLine}" +
            "기존 사이트 장바구니를 비우고 이번 주문 대상만 담을까요? 주문·결제는 실행하지 않습니다.",
            "기존 사이트 장바구니 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }
    private void RunCard(OrderCard card)
    {
        card.State = card.Supplier.Manual ? "WAITING_FOR_USER" : "PREPARING"; UpdateActions();
        // Post mock state transitions to the UI queue; no artificial delay or real I/O.
        BeginInvoke(() =>
        {
            if (closing || IsDisposed) return;
            card.State = card.Supplier.Manual && card.Lines[0].Product.Id == 10 ? "UNKNOWN" : "COMPLETED";
            UpdateActions();
            BeginInvoke(() =>
            {
                if (closing || IsDisposed) return;
                if (card.State == "COMPLETED")
                {
                    try { data.Complete(card.Lines); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { card.State = "FAILED"; batch = false; data.Unlock(card.Lines); busy = false; UpdateActions(); return; }
                }
                busy = false; UpdateActions(); if (batch) BeginInvoke(Advance);
            });
        });
    }
}
