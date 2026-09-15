namespace CafeOrder;

public record Supplier(string Id, string Name, bool Manual, decimal FreeShipping, bool SupportsNaverLogin = false);
public record Product(int Id, string Name, decimal Price, string PriceNote,
    Supplier Supplier, string Category, bool Available, int SampleOrderCount)
{
    public bool Available { get; set; } = Available;
    // Deliberately non-routable sample URLs; the mockup never opens these.
    public string Url => $"https://example.invalid/product/{Id}";
    public string PriceText => $"{Price:N0}원{(PriceNote.Length == 0 ? "" : $" ({PriceNote})")}";
}

public sealed class CartLine(Product product, int quantity)
{
    public Product Product { get; } = product;
    public int Quantity { get; set; } = quantity; // Used only for AUTO display and totals.
}

public sealed class SampleData
{
    public static readonly string[] Categories = ["전체", "원두", "일회용품", "유제품", "파우더", "베이스/농축액", "시럽/소스", "과일", "티백", "청", "디저트/스낵", "기타"];
    public List<Supplier> Suppliers { get; } =
    [
        new("mega", "메가커피", false, 50000), new("piece", "파미유", false, 99000),
        new("food", "푸드레인", false, 0), new("wym", "우양", false, 30000),
        new("nuldam", "늘담", false, 100000), new("coupang", "쿠팡", true, 0),
        new("naver", "네이버 스마트스토어", true, 0)
    ];
    public List<Product> Products { get; } = [];
    public List<CartLine> Cart { get; } = [];
    public Dictionary<string, decimal> Shipping { get; } = [];
    public event Action? CartChanged;
    public event Action<string>? ToastRequested;
    public event Action<string>? ProductAdded;
    public event Action<Product>? ProductChanged;
    private readonly HashSet<int> locked = [];
    public bool IsLocked(Product product) => locked.Contains(product.Id);
    public void Lock(IEnumerable<CartLine> lines) { foreach (var line in lines) locked.Add(line.Product.Id); Notify(); }
    public void Unlock(IEnumerable<CartLine> lines) { foreach (var line in lines) locked.Remove(line.Product.Id); Notify(); }
    public void Toast(string text) => ToastRequested?.Invoke(text);
    public void Recheck(Product product)
    {
        if (product.Supplier.Manual) return;
        product.Available = product.Id == 12; // Cream restocks; mango remains unavailable in the mock.
        ProductChanged?.Invoke(product); Toast("상품 정보를 다시 확인했습니다");
    }

    public SampleData()
    {
        foreach (var s in Suppliers) Shipping[s.Id] = s.FreeShipping;
        Add("포모나 코코렛 파우더 800g 2개세트 + 회원 구매시 560원 할인", 14500, 0, "파우더", "메가회원가");
        Add("까로망 요거트 파우더 1kg 1박스 12개", 108000, 0, "파우더");
        Add("아임요 샤인머스캣 에이드 1.5L 2개세트", 21800, 2, "베이스/농축액");
        Add("베버시티 후루티 파인애플 스무디", 13900, 3, "베이스/농축액");
        Add("복숭아 스무디", 9800, 3, "베이스/농축액");
        Add("국내생산 98mm(16/20/24온스 겸용) 아이스컵 십자뚜껑 100개", 3900, 6, "일회용품");
        Add("카페 블렌드 원두 1kg", 24000, 0, "원두");
        Add("디카페인 원두 500g", 18500, 2, "원두");
        Add("아이스컵 20온스 1,000개", 67000, 5, "일회용품");
        Add("개별포장 스트로우 500개", 4500, 6, "일회용품");
        Add("바리스타 우유 1L 12팩", 28500, 2, "유제품");
        Add("휘핑크림 1L", 8900, 2, "유제품", "", false);
        Add("말차 파우더 500g", 32000, 0, "파우더");
        Add("바닐라 시럽 1L", 12500, 0, "시럽/소스");
        Add("카라멜 소스 2kg", 18900, 2, "시럽/소스");
        Add("냉동 딸기 1kg", 7900, 3, "과일");
        Add("냉동 망고 1kg", 8500, 3, "과일", "", false);
        Add("얼그레이 티백 100입", 15000, 5, "티백");
        Add("캐모마일 티백 20입", 4900, 6, "티백");
        Add("수제 레몬청 2kg", 23000, 2, "청");
        Add("미니 버터 크루아상 30개", 27000, 1, "디저트/스낵");
        Add("비건 초콜릿 쿠키 24개입", 36000, 4, "디저트/스낵");
        Add("매장용 행주 10장", 2900, 5, "기타");
        Add("프리미엄 디저트 모음 60개", 158000, 1, "디저트/스낵");
        Cart.AddRange(new[] { new CartLine(Products[0], 2), new CartLine(Products[6], 1),
            new CartLine(Products[20], 1), new CartLine(Products[21], 1),
            new CartLine(Products[5], 0), new CartLine(Products[9], 0), new CartLine(Products[8], 0) });
    }

    private void Add(string name, decimal price, int supplier, string category, string note = "", bool available = true)
        => Products.Add(new(Products.Count + 1, name, price, note, Suppliers[supplier], category, available, (Products.Count * 7) % 31));

    public void AddToCart(Product p)
    {
        if (!p.Available || IsLocked(p)) return;
        var line = Cart.Find(x => x.Product.Id == p.Id);
        if (line == null) Cart.Add(new(p, p.Supplier.Manual ? 0 : 1));
        else if (!p.Supplier.Manual) line.Quantity++;
        Notify();
        ProductAdded?.Invoke(p.Supplier.Id); Toast("장바구니에 추가했습니다");
    }
    public void ChangeQuantity(CartLine line, int change)
    {
        if (line.Product.Supplier.Manual || IsLocked(line.Product) || !Cart.Contains(line)) return;
        line.Quantity = Math.Max(0, line.Quantity + change);
        if (line.Quantity == 0) { Remove(line); return; }
        Notify();
    }
    public void Remove(CartLine line)
    {
        if (IsLocked(line.Product) || !Cart.Remove(line)) return;
        Notify(); Toast("장바구니에서 삭제했습니다");
    }
    public void Complete(IEnumerable<CartLine> lines)
    {
        foreach (var line in lines.ToArray()) Cart.Remove(line);
        Notify();
    }
    public void Notify() => CartChanged?.Invoke();

    public decimal Subtotal(IEnumerable<CartLine> lines) => lines.Sum(x => x.Product.Price * x.Quantity);
    public decimal Shortfall(Supplier supplier, IEnumerable<CartLine> lines)
        => supplier.Manual ? 0 : Math.Max(0, Shipping[supplier.Id] - Subtotal(lines));

    public Supplier? DetectSupplier(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length != 0) return null;
        string host = uri.IdnHost.ToLowerInvariant();
        // Host-boundary matching only; never inspect URL contents by requesting the site.
        var suppliedDomains = new Dictionary<string, string>
        {
            ["megacoffee.co.kr"] = "mega", ["piececake.co.kr"] = "piece",
            ["foodrain.com"] = "food", ["nuldampartners.com"] = "nuldam"
        };
        foreach (var domain in suppliedDomains)
            if (host == domain.Key || host == "www." + domain.Key) return Suppliers.Single(x => x.Id == domain.Value);
        if (host == "coupang.com" || host.EndsWith(".coupang.com")) return Suppliers.Single(x => x.Id == "coupang");
        if (host is "smartstore.naver.com" or "brand.naver.com") return Suppliers.Single(x => x.Id == "naver");
        return Suppliers.FirstOrDefault(s => host == $"{s.Id}.example.invalid" || host == $"{s.Id.Replace("mega", "megacoffee")}.example.invalid");
    }
}
