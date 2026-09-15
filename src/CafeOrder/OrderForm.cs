namespace CafeOrder;

public sealed class OrderForm : Form
{
    private sealed class OrderCard(CartLine[] lines, string state)
    {
        public CartLine[] Lines { get; } = lines;
        public string State { get; set; } = state;
        public TableLayoutPanel View { get; set; } = null!;
        public Label Status { get; set; } = null!;
        public Button Action { get; set; } = null!;
    }
    private readonly List<OrderCard> cards = [];
    private readonly FlowLayoutPanel list = Ui.List("OrderCards");
    private readonly SampleData data;
    private readonly Button startAll;
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;
    private bool lifetimeDisposed;

    public OrderForm(SampleData data, IEnumerable<CartLine> lines, bool startManual = false)
    {
        this.data = data; Text = "주문 진행"; Font = new Font("Malgun Gothic", 12);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 600); MinimumSize = new Size(620, 440); StartPosition = FormStartPosition.CenterParent;
        foreach (var group in lines.GroupBy(x => x.Product.Supplier))
        {
            if (group.Key.Manual)
                foreach (var url in group.GroupBy(x => x.Product.Url)) cards.Add(new(url.ToArray(), "PENDING"));
            else cards.Add(new(group.ToArray(), group.Key.Id switch { "piece" => "FAILED", "nuldam" => "UNKNOWN", _ => "PENDING" }));
        }
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        var close = Ui.Button("닫기", Close); close.Dock = DockStyle.Fill;
        startAll = Ui.Button("전체 주문하기", () => StartAll(), true, "StartAllOrders"); startAll.Dock = DockStyle.Fill;
        footer.Controls.Add(close, 0, 0); footer.Controls.Add(startAll, 1, 0);
        Controls.Add(list); Controls.Add(footer);
        list.SuspendLayout();
        foreach (var card in cards) AddCard(card);
        list.ResumeLayout(true); UpdateActions();
        if (startManual) Shown += (_, _) => { if (cards.FirstOrDefault() is { } card) StartOne(card); };
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(620, area.Width), Math.Min(440, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !lifetimeDisposed) { lifetimeDisposed = true; lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
    private void AddCard(OrderCard card)
    {
        var s = card.Lines[0].Product.Supplier;
        card.Status = Ui.Text(""); card.Status.Name = $"OrderStatus_{card.Lines[0].Product.Id}";
        card.View = Ui.Column(Ui.Text(s.Name, true), card.Status);
        card.View.Name = $"Order_{s.Id}_{card.Lines[0].Product.Id}";
        foreach (var line in card.Lines)
        {
            ProductsView.AddRow(card.View, Ui.Text(line.Product.Name));
            ProductsView.AddRow(card.View, Ui.Text(s.Manual ? line.Product.PriceText : $"{line.Product.Price:N0}원 × {line.Quantity}"));
        }
        ProductsView.AddRow(card.View, Ui.Text(s.Manual ? "수량/옵션: 사이트에서 선택" : $"합계 {data.Subtotal(card.Lines):N0}원", !s.Manual));
        card.Action = Ui.Button(s.Manual ? "사이트에서 주문하기" : "주문하기", () => StartOne(card), true, $"OrderAction_{card.Lines[0].Product.Id}");
        ProductsView.AddRow(card.View, card.Action); list.Controls.Add(card.View);
    }
    private bool Eligible(OrderCard card) => card.State == "PENDING" && data.Shortfall(card.Lines[0].Product.Supplier, card.Lines) == 0;
    private void UpdateActions()
    {
        foreach (var card in cards)
        {
            var s = card.Lines[0].Product.Supplier; decimal shortage = data.Shortfall(s, card.Lines);
            card.Status.Text = card.State switch
            {
                "PREPARING" => "주문 진행 중...", "WAITING_FOR_USER" => "주문 확인 중...",
                "COMPLETED" => "✓ 주문 완료", "FAILED" => "주문실패 · 주문 내용을 확인해주세요.",
                "UNKNOWN" => "확인필요 · 주문 여부를 확인할 수 없습니다.", _ => "주문 대기"
            };
            card.Action.Text = shortage > 0 ? $"{shortage:N0}원 부족" : s.Manual ? "사이트에서 주문하기" : "주문하기";
            card.Action.BackColor = shortage > 0 ? Ui.Danger : Ui.Accent;
            card.Action.Enabled = !busy && Eligible(card);
        }
        startAll.Enabled = !busy && cards.Any(Eligible);
    }
    private async void StartOne(OrderCard card)
    {
        if (busy || !Eligible(card)) return;
        busy = true; UpdateActions();
        try { await RunCard(card); }
        catch (OperationCanceledException) { }
        finally { busy = false; if (!IsDisposed) UpdateActions(); }
    }
    private async void StartAll()
    {
        if (busy) return;
        busy = true; UpdateActions();
        try
        {
            foreach (var card in cards.ToArray())
            {
                if (!Eligible(card)) continue;
                // Opening a manual site still requires the user's own card click.
                // The batch pauses here; it never pretends to place that manual order.
                if (card.Lines[0].Product.Supplier.Manual) { list.ScrollControlIntoView(card.View); break; }
                await RunCard(card);
            }
        }
        catch (OperationCanceledException) { }
        finally { busy = false; if (!IsDisposed) UpdateActions(); }
    }
    private async Task RunCard(OrderCard card)
    {
        var token = lifetime.Token; bool manual = card.Lines[0].Product.Supplier.Manual;
        card.State = manual ? "WAITING_FOR_USER" : "PREPARING"; UpdateActions();
        await Task.Delay(700, token);
        // Deterministic mock confirmation only. No website calls or real order lookup.
        if (manual && card.Lines[0].Product.Id == 10)
        { card.State = "UNKNOWN"; UpdateActions(); return; }
        card.State = "COMPLETED"; UpdateActions(); await Task.Delay(550, token);
        data.Complete(card.Lines); cards.Remove(card); card.View.Dispose();
        if (cards.Count == 0) list.Controls.Add(Ui.Text("✓ 모든 주문이 완료되었습니다.", true));
    }
}
