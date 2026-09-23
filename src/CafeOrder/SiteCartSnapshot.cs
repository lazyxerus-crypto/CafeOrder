using System.Globalization;

namespace CafeOrder;

internal sealed record SiteCartTarget(int ProductId, string ExternalProductId, string ProductUrl,
    string Name, int Quantity, decimal Price, string OptionKey = "");
internal sealed record SiteCartAttempt(string AttemptId, string SupplierId,
    IReadOnlyList<SiteCartTarget> Targets);
internal sealed record StoredSiteCartAttempt(SiteCartAttempt Attempt, string State, string? Reason);

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
                        pieceNo != target.ExternalProductId) ||
                    supplierId == "nuldam" && (!NuldamProductLookup.TryProductUrl(target.ProductUrl, out _, out var nuldamNo) ||
                        nuldamNo != target.ExternalProductId))
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

    internal StoredSiteCartAttempt? ReadLatestSiteCartAttempt(string supplierId,
        IReadOnlyList<SiteCartTarget> currentTargets) => Access<StoredSiteCartAttempt?>(db =>
    {
        using var latest = Command(db, null, """
            SELECT AttemptId,State,Reason FROM SiteCartAttempts WHERE SupplierId=$supplier
            ORDER BY CreatedAtUtc DESC, rowid DESC LIMIT 1
            """, ("$supplier", supplierId));
        using var header = latest.ExecuteReader();
        if (!header.Read()) return null;
        string id = header.GetString(0), state = header.GetString(1);
        string? reason = header.IsDBNull(2) ? null : header.GetString(2);
        header.Close();
        using var items = Command(db, null, """
            SELECT ProductId,ExternalProductId,ProductUrl,Name,Quantity,Price,OptionKey
            FROM SiteCartAttemptItems WHERE AttemptId=$id
            """, ("$id", id));
        using var rows = items.ExecuteReader();
        var saved = new List<SiteCartTarget>();
        while (rows.Read())
            saved.Add(new(rows.GetInt32(0), rows.GetString(1), rows.GetString(2),
                rows.GetString(3), rows.GetInt32(4),
                decimal.Parse(rows.GetString(5), CultureInfo.InvariantCulture), rows.GetString(6)));
        if (saved.Count != currentTargets.Count) return null;
        var byId = currentTargets.ToDictionary(target => target.ProductId);
        if (!saved.All(item => byId.TryGetValue(item.ProductId, out var current) &&
            item.ExternalProductId == current.ExternalProductId && item.Quantity == current.Quantity &&
            item.Price == current.Price && item.OptionKey == current.OptionKey &&
            ProductUrlIdentity.Key(supplierId, item.ProductUrl) == ProductUrlIdentity.Key(supplierId, current.ProductUrl)))
            return null;
        return new(new(id, supplierId, saved), state, reason);
    });

    internal bool LatestSiteCartBrowserOpened(string supplierId) => Access(db =>
    {
        using var command = Command(db, null, """
            SELECT Reason FROM SiteCartAttempts WHERE SupplierId=$supplier
            ORDER BY CreatedAtUtc DESC, rowid DESC LIMIT 1
            """, ("$supplier", supplierId));
        return command.ExecuteScalar() as string == "BROWSER_OPENED";
    });

    internal void MarkSiteCartBrowserOpened(string attemptId)
    {
        Write((db, tx) =>
        {
            Execute(db, tx, """
                UPDATE SiteCartAttempts SET Reason='BROWSER_OPENED',UpdatedAtUtc=$now WHERE AttemptId=$id
                """, ("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                ("$id", attemptId));
            if (Number(db, tx, "SELECT changes()") != 1)
                throw new InvalidDataException("사이트 장바구니 진행 기록을 찾을 수 없습니다.");
            return 0;
        });
    }
}
