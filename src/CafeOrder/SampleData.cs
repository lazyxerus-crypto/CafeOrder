namespace CafeOrder;

public record Supplier(string Id, string Name, bool Manual, decimal FreeShipping, bool SupportsNaverLogin = false);
public record Product(int Id, string Name, decimal Price, string PriceNote,
    Supplier Supplier, string Category, bool Available, int SampleOrderCount)
{
    public string Name { get; set; } = Name;
    public decimal Price { get; set; } = Price;
    public Supplier Supplier { get; set; } = Supplier;
    public bool Available { get; set; } = Available;
    public string Category { get; set; } = Category;
    public bool IsActive { get; set; } = true;
    // Non-routable sample URLs. Only an explicit product-link command invokes the default browser.
    public string Url { get; set; } = $"https://example.invalid/product/{Id}";
    public string? DisplayPrice { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageCachePath { get; set; }
    public string? ManualImagePath { get; set; }
    public string DataOrigin { get; set; } = "Sample";
    public string PriceText => DisplayPrice ?? $"{Price:N0}원{(PriceNote.Length == 0 ? "" : $" ({PriceNote})")}";
}

public sealed class CartLine(Product product, int quantity)
{
    public Product Product { get; } = product;
    public int Quantity { get; set; } = quantity; // Used only for AUTO display and totals.
}

public sealed partial class SampleData
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
    public event Action<Product>? ProductAdded;
    public event Action<Product>? ProductChanged;
    public event Action? CatalogChanged;
    public LocalState Store { get; }
    private readonly HashSet<int> locked = [];
    public bool IsLocked(Product product) => locked.Contains(product.Id);
    public void Lock(IEnumerable<CartLine> lines) { foreach (var line in lines) locked.Add(line.Product.Id); Notify(); }
    public void Unlock(IEnumerable<CartLine> lines) { foreach (var line in lines) locked.Remove(line.Product.Id); Notify(); }
    public void Recheck(Product product)
    {
        if (product.Supplier.Manual) return;
        bool previous = product.Available;
        product.Available = product.Id == 12; // Cream restocks; mango remains unavailable in the mock.
        try { Store.Database.SaveProduct(product); } catch { product.Available = previous; throw; }
        ProductChanged?.Invoke(product);
    }

    public SampleData(LocalState? store = null)
    {
        Store = store ?? new LocalState();
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
        Store.Database.Initialize(Store, Suppliers, Products);
        foreach (var saved in Store.Database.ReadProducts(Suppliers))
        {
            int index = Products.FindIndex(p => p.Id == saved.Id);
            if (index < 0) Products.Add(saved);
            else Products[index] = saved with { SampleOrderCount = Products[index].SampleOrderCount };
        }
        Cart.AddRange(Store.Database.ReadCart(Products));
    }

    private void Add(string name, decimal price, int supplier, string category, string note = "", bool available = true)
        => Products.Add(new(Products.Count + 1, name, price, note, Suppliers[supplier], category, available, (Products.Count * 7) % 31));

    public void AddToCart(Product p)
    {
        if (!p.IsActive || !p.Available || IsLocked(p)) return;
        int quantity = Store.Database.AddToCart(p);
        var line = Cart.Find(x => x.Product.Id == p.Id);
        if (line == null) line = new(p, quantity);
        else line.Quantity = quantity;
        Cart.Remove(line); Cart.Insert(0, line);
        Store.Log.Write(LogLevel.INFO, "CART_ITEM_ADDED", "상품을 로컬 장바구니에 담았습니다.",
            supplier: p.Supplier.Id, productId: p.Id, result: "SUCCESS");
        Notify();
        ProductAdded?.Invoke(p);
    }
    public void ChangeQuantity(CartLine line, int change)
    {
        if (line.Product.Supplier.Manual || IsLocked(line.Product) || !Cart.Contains(line)) return;
        int quantity = Math.Max(0, checked(line.Quantity + change));
        Store.Database.SetQuantity(line.Product.Id, quantity);
        line.Quantity = quantity;
        if (quantity == 0) Cart.Remove(line);
        Store.Log.Write(LogLevel.INFO, quantity == 0 ? "CART_ITEM_REMOVED" : "CART_QUANTITY_CHANGED",
            quantity == 0 ? "상품을 로컬 장바구니에서 제거했습니다." : "로컬 장바구니 수량을 변경했습니다.",
            supplier: line.Product.Supplier.Id, productId: line.Product.Id, result: "SUCCESS");
        Notify();
    }
    public void Remove(CartLine line)
    {
        if (IsLocked(line.Product) || !Cart.Contains(line)) return;
        Store.Database.RemoveCart([line.Product.Id]); Cart.Remove(line);
        Store.Log.Write(LogLevel.INFO, "CART_ITEM_REMOVED", "상품을 로컬 장바구니에서 제거했습니다.",
            supplier: line.Product.Supplier.Id, productId: line.Product.Id, result: "SUCCESS");
        Notify();
    }
    public void Complete(IEnumerable<CartLine> lines)
    {
        var selected = lines.ToArray();
        Store.Database.RemoveCart(selected.Select(line => line.Product.Id));
        foreach (var line in selected) Cart.Remove(line);
        foreach (var line in selected)
            Store.Log.Write(LogLevel.INFO, "CART_ITEM_REMOVED", "주문 진행 대상으로 옮기며 로컬 장바구니에서 제거했습니다.",
                supplier: line.Product.Supplier.Id, productId: line.Product.Id, result: "SUCCESS", reason: "ORDER_PROGRESS");
        Notify();
    }
    public void Notify() => CartChanged?.Invoke();
    public void SetActive(Product product, bool active)
    {
        bool previous = product.IsActive; product.IsActive = active;
        try { Store.Database.SaveProduct(product); } catch { product.IsActive = previous; throw; }
        Store.Log.Write(LogLevel.INFO, active ? "PRODUCT_RESTORED" : "PRODUCT_DELETED",
            active ? "상품 삭제 상태를 되돌렸습니다." : "상품을 삭제 상태로 저장했습니다.",
            supplier: product.Supplier.Id, productId: product.Id, result: "SUCCESS");
        ProductChanged?.Invoke(product);
    }
    public void SetCategory(Product product, string category)
    {
        if (!Categories.Skip(1).Contains(category)) return;
        string previous = product.Category; product.Category = category;
        try { Store.Database.SaveProduct(product); } catch { product.Category = previous; throw; }
        Store.Log.Write(LogLevel.INFO, "PRODUCT_UPDATED", "상품 카테고리를 저장했습니다.",
            supplier: product.Supplier.Id, productId: product.Id, result: "SUCCESS", reason: "CATEGORY_CHANGED");
        ProductChanged?.Invoke(product); CatalogChanged?.Invoke(); Notify();
    }
    public void SetManualImage(Product product, string? path)
    {
        string? previous = product.ManualImagePath; product.ManualImagePath = path;
        try { Store.Database.SaveProduct(product); } catch { product.ManualImagePath = previous; throw; }
        Store.Log.Write(LogLevel.INFO, "PRODUCT_IMAGE_CHANGED",
            path == null ? "수동 이미지를 해제하고 저장된 웹 이미지 사용으로 전환했습니다." : "수동 이미지 경로를 저장했습니다.",
            supplier: product.Supplier.Id, productId: product.Id, result: "SUCCESS");
        ProductChanged?.Invoke(product); Notify();
    }
    public Product RegisterMock(string url, string category)
    {
        if (MegaCoffeeProductLookup.IsMegaHost(url))
            throw new ArgumentException("메가커피 실제 상품은 로그인된 페이지 조회 후 등록해야 합니다.");
        var seller = DetectSupplier(url) ?? throw new ArgumentException("지원하지 않는 상품 링크입니다");
        var source = Products.First(p => p.Supplier.Id == seller.Id);
        var product = Store.Database.InsertProduct(new Product(0, source.Name, source.Price, source.PriceNote, seller, category, true, 0)
        { Url = url.Trim(), DisplayPrice = source.PriceText });
        Products.Add(product);
        Store.Log.Write(LogLevel.INFO, "PRODUCT_REGISTERED", "상품 카드를 로컬 DB에 등록했습니다.",
            supplier: seller.Id, productId: product.Id, result: "SUCCESS", reason: "MOCK_REGISTRATION");
        return product; // The same draft control adopts this record without refreshing or sorting the catalog.
    }
    internal Product RegisterMegaProduct(MegaCoffeeProductSnapshot snapshot, string category, string imagePath)
    {
        if (!Categories.Skip(1).Contains(category) || !File.Exists(imagePath))
            throw new ArgumentException("상품 분류 또는 이미지 파일이 올바르지 않습니다.");
        var seller = Suppliers.Single(s => s.Id == "mega");
        var product = Store.Database.InsertProduct(new Product(0, snapshot.Name, snapshot.Price, "", seller, category, snapshot.Available, 0)
        {
            Url = snapshot.ProductUrl, DisplayPrice = snapshot.DisplayPrice,
            ImageUrl = snapshot.ImageUrl, ImageCachePath = imagePath
        });
        Products.Add(product);
        Store.Log.Write(LogLevel.INFO, "PRODUCT_REGISTERED", "실제 조회한 메가커피 상품을 로컬 DB에 등록했습니다.",
            supplier: seller.Id, productId: product.Id, result: "SUCCESS", reason: "MEGA_LOOKUP");
        return product;
    }
    private IProductWorkbook Workbook() => new ClosedXmlProductWorkbook(Suppliers, Categories);
    public ProductTransferRow[] ExportRows() => Store.Database.ReadProducts(Suppliers)
        .Select(p => new ProductTransferRow(p.Id, p.Supplier.Name, p.Name, p.Price, p.PriceText, p.Category, p.Url, p.IsActive)).ToArray();
    internal int ExportWorkbook(string path)
    {
        try
        {
            var rows = ExportRows(); Workbook().Export(path, rows);
            Store.Log.Write(LogLevel.INFO, "XLSX_EXPORT_SUCCESS", $"상품 {rows.Length}건을 XLSX로 내보냈습니다.", result: "SUCCESS");
            return rows.Length;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { Store.Log.Write(LogLevel.ERROR, "XLSX_EXPORT_FAILED", "상품 XLSX 내보내기에 실패했습니다.", result: "FAILED", error: ex); throw; }
    }
    internal ProductImportPlan PrepareImport(string path)
    {
        var plan = BuildImportPlan(SelectImportRows(Workbook().Import(path), false), null);
        LogImportPreview(plan);
        return plan;
    }
    internal void ApplyImport(ProductImportPlan plan) => PublishImportedProducts(plan, WriteImport(plan));
    internal async Task ApplyImportAsync(ProductImportPlan plan)
    {
        var written = await Task.Run(() => WriteImport(plan));
        PublishImportedProducts(plan, written);
    }
    private IReadOnlyList<Product> WriteImport(ProductImportPlan plan)
    {
        if (plan.Applied || plan.Issues.Count != 0) throw new InvalidDataException("검증되지 않았거나 이미 반영한 파일입니다.");
        var moved = new List<string>();
        IReadOnlyList<Product> written;
        try
        {
            foreach (var change in plan.Changes.Where(change => change.PendingImagePath != null))
            {
                string final = change.Proposed.ImageCachePath!;
                Directory.CreateDirectory(Path.GetDirectoryName(final)!);
                if (!File.Exists(change.PendingImagePath))
                    throw new InvalidDataException($"{change.SheetRow}행 · 이미지: 준비된 이미지가 없습니다. 다시 가져오세요.");
                File.Move(change.PendingImagePath!, final);
                moved.Add(final);
            }
            written = Store.Database.ApplyImport(plan.Changes);
        }
        catch (Exception ex)
        {
            foreach (string path in moved) if (File.Exists(path)) File.Delete(path);
            var match = System.Text.RegularExpressions.Regex.Match(ex.Message, @"^(\d+)행");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int failedRow))
                Store.Log.Write(LogLevel.ERROR, "XLSX_ROW_FAILED", "XLSX 행의 이미지 또는 DB 반영에 실패했습니다.",
                    row: failedRow, result: "FAILED", reason: "APPLY_FAILED", error: ex);
            Store.Log.Write(LogLevel.ERROR, "XLSX_IMPORT_FAILED", "상품 XLSX 변경에 실패해 반영을 완료하지 않았습니다.",
                result: "FAILED", error: ex);
            throw;
        }
        return written;
    }
    private void PublishImportedProducts(ProductImportPlan plan, IReadOnlyList<Product> written)
    {
        plan.Applied = true;
        for (int index = 0; index < written.Count; index++)
        {
            var change = plan.Changes[index]; var product = written[index];
            Store.Log.Write(LogLevel.INFO, change.Existing == null ? "XLSX_ROW_ADDED" : "XLSX_ROW_UPDATED",
                change.Existing == null ? "XLSX 상품 행을 신규 등록했습니다." : "XLSX 상품 행으로 기존 상품을 수정했습니다.",
                supplier: product.Supplier.Id, productId: product.Id, row: change.SheetRow, result: "SUCCESS");
        }
        Store.Log.Write(LogLevel.INFO, "XLSX_IMPORT_APPLIED",
            $"XLSX 반영 완료: 추가 {plan.Added}건, 수정 {plan.Updated}건, 건너뜀 {plan.Skipped}건.", result: "SUCCESS");
        for (int index = 0; index < written.Count; index++)
        {
            var existing = plan.Changes[index].Existing; var product = written[index];
            if (existing == null) { Products.Add(product); continue; }
            existing.Name = product.Name; existing.Price = product.Price; existing.DisplayPrice = product.DisplayPrice;
            existing.Supplier = product.Supplier; existing.Category = product.Category; existing.Url = product.Url;
            existing.IsActive = product.IsActive; existing.Available = product.Available;
            existing.ImageUrl = product.ImageUrl; existing.ImageCachePath = product.ImageCachePath;
            existing.ManualImagePath = product.ManualImagePath; ProductChanged?.Invoke(existing);
        }
        CatalogChanged?.Invoke();
        if (plan.Changes.Any(change => change.Existing != null && Cart.Any(line => line.Product.Id == change.Existing.Id))) Notify();
    }

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
