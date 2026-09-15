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

    public OrderForm(SampleData data, IEnumerable<CartLine> lines, bool startManual = false)
    {
        this.data = data; targets = lines.ToHashSet(); Text = "주문 진행"; Font = new Font("Malgun Gothic", 12);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 600); MinimumSize = new Size(620, 440); StartPosition = FormStartPosition.CenterParent;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft };
        var close = Ui.Button("닫기", Close, name: "CloseOrders");
        startAll = Ui.Button("전체 주문하기", StartAll, true, "StartAllOrders");
        foreach (var button in new[] { close, startAll }) { button.AutoSize = false; button.Size = new Size(170, 42); }
        footer.Controls.Add(startAll); footer.Controls.Add(close); Controls.Add(list); Controls.Add(footer);
        foreach (var group in targets.GroupBy(x => x.Product.Supplier))
        {
            if (group.Key.Manual)
                foreach (var url in group.GroupBy(x => x.Product.Url)) AddCard(group.Key, url.ToList(), "PENDING");
            else AddCard(group.Key, group.ToList(), group.Key.Id switch { "piece" => "FAILED", "nuldam" => "UNKNOWN", _ => "PENDING" });
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
    private void AddCard(Supplier supplier, List<CartLine> lines, string state)
    {
        var card = new OrderCard(supplier, state) { Lines = lines };
        card.Status = Ui.Text(""); card.Status.Name = $"OrderStatus_{lines[0].Product.Id}";
        card.Items = Ui.Column(); card.Items.Padding = Padding.Empty;
        card.Total = Ui.Text("", true);
        card.Action = Ui.Button(supplier.Manual ? "판매처에서 주문하기" : "주문하기", () => StartOne(card), true, $"OrderAction_{lines[0].Product.Id}");
        card.Action.AutoSize = false; card.Action.Size = new Size(220, 44);
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
    private bool Eligible(OrderCard card) => card.State == "PENDING" && data.Shortfall(card.Supplier, card.Lines) == 0;
    private void UpdateActions()
    {
        foreach (var card in cards)
        {
            decimal shortage = data.Shortfall(card.Supplier, card.Lines);
            card.Status.Text = card.State switch
            {
                "PREPARING" => "주문 진행 중...", "WAITING_FOR_USER" => "주문 확인 중...", "COMPLETED" => "✓ 주문 완료",
                "FAILED" => "주문실패 · 주문 내용을 확인해주세요.", "UNKNOWN" => "확인필요 · 주문 여부를 확인할 수 없습니다.", _ => "주문 대기"
            };
            card.Action.Text = shortage > 0 ? $"{shortage:N0}원 부족" : card.Supplier.Manual ? "판매처에서 주문하기" : "주문하기";
            card.Action.BackColor = shortage > 0 ? Ui.Danger : Ui.Accent; card.Action.Enabled = !busy && Eligible(card);
        }
        startAll.Enabled = !busy && !batch && cards.Any(Eligible);
    }
    private void StartOne(OrderCard card)
    {
        if (busy || !Eligible(card)) return;
        busy = true; data.Lock(card.Lines); RunCard(card);
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
        busy = true; RunCard(card);
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
                if (card.State == "COMPLETED") data.Complete(card.Lines);
                busy = false; UpdateActions(); if (batch) BeginInvoke(Advance);
            });
        });
    }
}
