namespace CafeOrder;

public sealed class OrderForm : Form
{
    private sealed class OrderCard(CartLine[] lines, string state)
    {
        public CartLine[] Lines { get; } = lines;
        public string State { get; set; } = state;
    }
    private readonly List<OrderCard> cards = [];
    private readonly FlowLayoutPanel list = Ui.List("OrderCards");
    private readonly SampleData data;
    private bool busy;

    public OrderForm(SampleData data, IEnumerable<CartLine> lines)
    {
        this.data = data; Text = "주문 진행 · UI 목업"; Font = new Font("Malgun Gothic", 12);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 600); MinimumSize = new Size(620, 440); StartPosition = FormStartPosition.CenterParent;
        foreach (var group in lines.GroupBy(x => x.Product.Supplier))
        {
            if (group.Key.Manual)
                foreach (var url in group.GroupBy(x => x.Product.Url)) cards.Add(new(url.ToArray(), "PENDING"));
            else cards.Add(new(group.ToArray(), group.Key.Id switch { "piece" => "FAILED", "nuldam" => "UNKNOWN", _ => "PENDING" }));
        }
        var header = Ui.Text("주문 진행 · 샘플\n위에서 아래로 진행합니다. FAILED / UNKNOWN은 남겨둡니다.", true);
        var close = Ui.Button("닫기", Close); close.Dock = DockStyle.Bottom;
        Controls.Add(list); Controls.Add(header); Controls.Add(close); Render();
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(620, area.Width), Math.Min(440, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
    }
    private void Render()
    {
        list.SuspendLayout(); Ui.Clear(list);
        var current = cards.FirstOrDefault(x => x.State == "PENDING");
        foreach (var card in cards)
        {
            var s = card.Lines[0].Product.Supplier;
            var view = Ui.Column(Ui.Text($"{(card == current ? "현재 처리" : "대기 / 확인")} · {card.State}", true), Ui.Text(s.Name, true));
            view.Name = $"Order_{s.Id}_{card.Lines[0].Product.Id}";
            foreach (var line in card.Lines)
                ProductsView.AddRow(view, Ui.Text(line.Product.Name + (s.Manual ? "" : $" · {line.Quantity}개")));
            if (card.State is "FAILED" or "UNKNOWN")
                ProductsView.AddRow(view, Ui.Text(card.State == "FAILED" ? "샘플 실패 상태 · 이 카드는 유지됩니다." : "샘플 결과 불명 · 재결제하지 않고 확인이 필요합니다."));
            else if (card.State == "COMPLETED") ProductsView.AddRow(view, Ui.Text("✓ 주문 완료", true));
            else
            {
                if (s.Manual) ProductsView.AddRow(view, Ui.Button("사이트에서 주문하기", () => Ui.Notice(this, "목업 안내: 실제 브라우저를 열거나 주문하지 않습니다.")));
                var complete = Ui.Button(s.Manual ? "주문완료로 기록" : "주문하기 · 샘플 성공", () => Complete(card), true, $"Complete_{card.Lines[0].Product.Id}");
                complete.Enabled = !busy && card == current;
                ProductsView.AddRow(view, complete);
            }
            list.Controls.Add(view);
        }
        if (cards.Count == 0) list.Controls.Add(Ui.Text("✓ 모든 샘플 주문이 완료되었습니다.", true));
        list.ResumeLayout(true); Ui.Fit(list);
    }
    private async void Complete(OrderCard card)
    {
        if (busy || card.State != "PENDING") return;
        busy = true; card.State = "COMPLETED"; Render();
        // UI-only feedback delay; no network, payment, persistence or background automation.
        await Task.Delay(650);
        data.Complete(card.Lines); cards.Remove(card); busy = false;
        if (!IsDisposed) Render();
    }
}
