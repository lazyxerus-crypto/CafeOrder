using System.Globalization;

namespace CafeOrder;

internal sealed record SiteCartTarget(int ProductId, string ExternalProductId, string ProductUrl,
    string Name, int Quantity, decimal Price, string OptionKey = "");
internal sealed record SiteCartAttempt(string AttemptId, string SupplierId,
    IReadOnlyList<SiteCartTarget> Targets);

internal sealed partial class CatalogDatabase
{
    internal SiteCartAttempt CreateSiteCartAttempt(string supplierId, IReadOnlyList<SiteCartTarget> targets)
    {
        if (targets.Count == 0 || targets.Any(x => x.Quantity <= 0 || x.ProductId <= 0 ||
            string.IsNullOrWhiteSpace(x.ExternalProductId) || string.IsNullOrWhiteSpace(x.ProductUrl)) ||
            targets.Select(x => x.ProductId).Distinct().Count() != targets.Count ||
            targets.Select(x => (x.ExternalProductId, x.OptionKey)).Distinct().Count() != targets.Count)
            throw new InvalidDataException("사이트 장바구니 대상이 올바르지 않습니다.");
        string attemptId = Guid.NewGuid().ToString("N");
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return Write((db, tx) =>
        {
            Execute(db, tx, "INSERT INTO SiteCartAttempts VALUES($id,$supplier,'PREPARING',$now,$now,NULL)",
                ("$id", attemptId), ("$supplier", supplierId), ("$now", now));
            foreach (var target in targets)
            {
                using var check = Command(db, tx, """
                    SELECT c.Quantity,p.ProductUrl FROM CartItems c JOIN Products p ON p.ProductId=c.ProductId
                    WHERE c.ProductId=$id AND p.SupplierId=$supplier
                    """, ("$id", target.ProductId), ("$supplier", supplierId));
                using var current = check.ExecuteReader();
                if (!current.Read() || current.GetInt32(0) != target.Quantity ||
                    !string.Equals(ProductUrlIdentity.Key(supplierId, current.GetString(1)),
                        ProductUrlIdentity.Key(supplierId, target.ProductUrl), StringComparison.OrdinalIgnoreCase) ||
                    supplierId == "mega" && ProductUrlIdentity.MegaGoodsNo(supplierId, target.ProductUrl) != target.ExternalProductId ||
                    supplierId == "piece" && (!PieceCakeProductLookup.TryProductUrl(target.ProductUrl, out _, out var pieceNo) ||
                        pieceNo != target.ExternalProductId))
                    throw new InvalidDataException("주문 시작 전 장바구니 상품 또는 수량이 변경됐습니다.");
                Execute(db, tx, """
                    INSERT INTO SiteCartAttemptItems(AttemptId,ProductId,ExternalProductId,ProductUrl,Name,Quantity,Price,OptionKey)
                    VALUES($attempt,$id,$external,$url,$name,$quantity,$price,$option)
                    """, ("$attempt", attemptId), ("$id", target.ProductId),
                    ("$external", target.ExternalProductId), ("$url", target.ProductUrl),
                    ("$name", target.Name), ("$quantity", target.Quantity),
                    ("$price", target.Price.ToString(CultureInfo.InvariantCulture)), ("$option", target.OptionKey));
            }
            return new SiteCartAttempt(attemptId, supplierId, targets.ToArray());
        });
    }

    internal void SetSiteCartAttemptState(string attemptId, string state, string? reason = null)
    {
        if (state is not ("PREPARING" or "WAITING_FOR_USER" or "READY" or "FAILED" or "UNKNOWN"))
            throw new ArgumentOutOfRangeException(nameof(state));
        Write((db, tx) =>
        {
            Execute(db, tx, """
                UPDATE SiteCartAttempts SET State=$state, Reason=$reason, UpdatedAtUtc=$now WHERE AttemptId=$id
                """, ("$state", state), ("$reason", reason),
                ("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)), ("$id", attemptId));
            if (Number(db, tx, "SELECT changes()") != 1)
                throw new InvalidDataException("사이트 장바구니 진행 기록을 찾을 수 없습니다.");
            return 0;
        });
    }
}
