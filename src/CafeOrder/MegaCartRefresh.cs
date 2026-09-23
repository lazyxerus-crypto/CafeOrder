namespace CafeOrder;

public sealed partial class SampleData
{
    private readonly HashSet<int> megaCartRefreshing = [];
    private readonly HashSet<int> megaCartRetry = [];
    private readonly HashSet<int> megaCartFailed = [];

    internal bool IsMegaCartLookupPending(Product product) => megaCartRefreshing.Contains(product.Id);
    internal bool MegaCartLookupFailed(Product product) => megaCartFailed.Contains(product.Id);
    internal bool CanStartLocalOrder(CartLine line) => line.Product.Available &&
        !IsMegaCartLookupPending(line.Product) && !MegaCartLookupFailed(line.Product);

    internal async Task RefreshFirstMegaCartAsync(Product product, CartLine addedLine,
        Func<string, Task<MegaProductLookupResult>> lookup)
        => await RefreshFirstSiteCartAsync(product, addedLine, lookup);

    internal Task RefreshFirstPieceCartAsync(Product product, CartLine addedLine,
        Func<string, Task<MegaProductLookupResult>> lookup)
        => RefreshFirstSiteCartAsync(product, addedLine, lookup);

    private async Task RefreshFirstSiteCartAsync(Product product, CartLine addedLine,
        Func<string, Task<MegaProductLookupResult>> lookup)
    {
        if (!megaCartRefreshing.Add(product.Id))
        {
            // Removal followed by another first add waits for the current request to finish.
            megaCartRetry.Add(product.Id);
            return;
        }
        try
        {
            CartLine? line = addedLine;
            do
            {
                megaCartFailed.Remove(product.Id);
                Notify();
                await RefreshSiteCartOnceAsync(product, line, lookup);
                line = megaCartRetry.Remove(product.Id)
                    ? Cart.FirstOrDefault(item => item.Product.Id == product.Id) : null;
            } while (line != null);
        }
        finally
        {
            megaCartRefreshing.Remove(product.Id);
            Notify();
        }
    }

    private async Task RefreshSiteCartOnceAsync(Product product, CartLine line,
        Func<string, Task<MegaProductLookupResult>> lookup)
    {
        string supplierId = product.Supplier.Id;
        string supplierName = product.Supplier.Name;
        string? identity = ProductUrlIdentity.Key(supplierId, product.Url);
        string? goodsNo = ProductUrlIdentity.MegaGoodsNo(supplierId, product.Url);
        Store.Log.Write(LogLevel.INFO, "CART_FIRST_LOOKUP_STARTED", $"{supplierName} 상품 최초 담기 정보를 확인하기 시작했습니다.",
            supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "STARTED");
        MegaProductLookupResult result;
        try { result = await lookup(product.Url); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (Cart.Contains(line)) { megaCartFailed.Add(product.Id); Notify(); }
            Store.Log.Write(LogLevel.ERROR, "CART_FIRST_LOOKUP_FAILED", $"{supplierName} 최초 담기 브라우저 조회에 실패했습니다.",
                supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "FAILED",
                reason: "LOOKUP_EXCEPTION", error: ex);
            return;
        }
        if (!Cart.Contains(line) || !Products.Contains(product) || !product.IsActive ||
            ProductUrlIdentity.Key(supplierId, product.Url) != identity)
        {
            Store.Log.Write(LogLevel.INFO, "CART_FIRST_LOOKUP_DISCARDED", "조회 중 상품 또는 장바구니 상태가 바뀌어 결과를 반영하지 않았습니다.",
                supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "SKIPPED", reason: "STALE_CART_ADD");
            return;
        }
        if (result.Status != MegaProductLookupStatus.Success || result.Product is not { } snapshot)
        {
            megaCartFailed.Add(product.Id); Notify();
            string reason = result.Status switch
            {
                MegaProductLookupStatus.LoginRequired => "LOGIN_REQUIRED",
                MegaProductLookupStatus.InvalidUrl => "INVALID_URL",
                _ when result.Reason?.Contains("판매가", StringComparison.Ordinal) == true ||
                    result.Reason?.Contains("가격", StringComparison.Ordinal) == true => "PRICE_UNAVAILABLE",
                _ when result.Reason?.Contains("이미지", StringComparison.Ordinal) == true => "IMAGE_UNAVAILABLE",
                _ when result.Reason?.Contains("상품명", StringComparison.Ordinal) == true => "NAME_UNAVAILABLE",
                _ when result.Reason?.Contains("품절", StringComparison.Ordinal) == true => "STOCK_UNAVAILABLE",
                _ when result.ErrorType == "PlaywrightException" => "BROWSER_OR_NETWORK_ERROR",
                _ => "PAGE_OR_BROWSER_ERROR"
            };
            Store.Log.Write(LogLevel.WARN, "CART_FIRST_LOOKUP_FAILED", $"{supplierName} 최초 담기 상품정보를 확인하지 못해 저장값을 유지했습니다.",
                supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "FAILED", reason: reason);
            return;
        }
        if (string.IsNullOrWhiteSpace(snapshot.Name) || string.IsNullOrWhiteSpace(snapshot.DisplayPrice) ||
            snapshot.Price < 0 || snapshot.ImageBytes.Length == 0 || string.IsNullOrWhiteSpace(snapshot.ImageUrl) ||
            ProductUrlIdentity.Key(supplierId, snapshot.ProductUrl) != identity)
        {
            megaCartFailed.Add(product.Id); Notify();
            Store.Log.Write(LogLevel.WARN, "CART_FIRST_LOOKUP_FAILED", "조회 결과에 필수 상품정보가 없어 저장값을 유지했습니다.",
                supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "FAILED", reason: "INCOMPLETE_PRODUCT");
            return;
        }
        string imagePath = Store.NewWebImagePath();
        bool saved = false;
        try
        {
            await Task.Run(() => ManualImages.Save(snapshot.ImageBytes, imagePath));
            if (!Cart.Contains(line) || !Products.Contains(product) || !product.IsActive ||
                ProductUrlIdentity.Key(supplierId, product.Url) != identity)
            {
                Store.Log.Write(LogLevel.INFO, "CART_FIRST_LOOKUP_DISCARDED", "이미지 처리 중 장바구니 상태가 바뀌어 결과를 반영하지 않았습니다.",
                    supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "SKIPPED", reason: "STALE_CART_ADD");
                return;
            }
            decimal oldPrice = product.Price;
            bool oldAvailable = product.Available;
            var updated = product with
            {
                Name = snapshot.Name, Price = snapshot.Price, DisplayPrice = snapshot.DisplayPrice,
                Url = snapshot.ProductUrl, ImageUrl = snapshot.ImageUrl, ImageCachePath = imagePath,
                Available = snapshot.Available, LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow
            };
            Store.Database.SaveProduct(updated);
            saved = true;
            product.Name = updated.Name; product.Price = updated.Price;
            product.DisplayPrice = updated.DisplayPrice; product.Url = updated.Url;
            product.ImageUrl = updated.ImageUrl; product.ImageCachePath = updated.ImageCachePath;
            product.Available = updated.Available;
            product.LastSuccessfulCheckAtUtc = updated.LastSuccessfulCheckAtUtc;
            Store.Log.Write(LogLevel.INFO, "CART_FIRST_LOOKUP_SUCCESS", $"{supplierName} 최초 담기 상품정보를 SQLite에 갱신했습니다.",
                supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "SUCCESS",
                oldPrice: oldPrice, newPrice: product.Price);
            if (oldPrice != product.Price)
                Store.Log.Write(LogLevel.INFO, "CART_FIRST_PRICE_CHANGED", $"{supplierName} 조회 가격이 저장 가격과 달라 상품·장바구니 합계를 갱신했습니다.",
                    supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "CHANGED",
                    oldPrice: oldPrice, newPrice: product.Price);
            if (oldAvailable != product.Available)
                Store.Log.Write(LogLevel.INFO, "CART_FIRST_STOCK_CHANGED", $"{supplierName} 조회 품절 상태가 변경됐습니다.",
                    supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: product.Available ? "AVAILABLE" : "SOLD_OUT");
            ProductChanged?.Invoke(product);
            Notify();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!saved)
            {
                megaCartFailed.Add(product.Id); Notify();
                Store.Log.Write(LogLevel.ERROR, "CART_FIRST_LOOKUP_FAILED", "조회 이미지 또는 상품정보를 저장하지 못해 기존 값을 유지했습니다.",
                    supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "FAILED",
                    reason: ex is ImageMagick.MagickException ? "IMAGE_CONVERSION_FAILED" : "LOCAL_SAVE_FAILED", error: ex);
            }
            else Store.Log.Write(LogLevel.ERROR, "CART_FIRST_UI_REFRESH_FAILED", "상품정보 저장 후 화면 갱신에 실패했습니다.",
                supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "FAILED", error: ex);
        }
        finally
        {
            if (!saved && File.Exists(imagePath))
                try { File.Delete(imagePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Store.Log.Write(LogLevel.ERROR, "IMAGE_TEMP_CLEANUP_FAILED", "실패한 조회 이미지 임시 파일을 정리하지 못했습니다.",
                    supplier: supplierId, productId: product.Id, goodsNo: goodsNo, result: "FAILED", error: ex); }
        }
    }
}
