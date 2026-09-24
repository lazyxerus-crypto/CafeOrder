using CafeOrder;

internal static class SearchChecks
{
    internal static void Run()
    {
        var seller = new Supplier("mega", "메가커피", false, 0);
        string[] names =
        [
            "까로망 초코소스 2kg", "까로망 다크 초코렛 소스", "까로망 바닐라 소스",
            "포모나 코코렛 파우더", "기초코어 분석기", "쵸코 쿠키", "초콜릿 파우더",
            "아임요 토피넛 파우더", "땅콩 스프레드", "피넛 버터", "바나나 파우더",
            "바나나맛 파우더", "바나나 분말", "바나나 우유", "바나나 과자",
            "딸기 파우더", "콘차우더", "PET 투명컵용 뚜껑 D92 1박스",
            "테이크아웃 컵뚜껑 92파이 중평리드", "92파이 돔리드", "90mm 종이컵 뚜껑",
            "192파이 뚜껑", "블랙티 홍차", "잉글리쉬 브렉퍼스트 티",
            "얼그레이 티백", "캐모마일 티", "페퍼민트 티",
            "냉동 망고 1kg", "냉동 딸기 1kg", "냉동 츄러스", "망고 스무디",
            "응오이사오 프엉남 연유 대체품 1.284kg", "샤인머스캣 에이드",
            "파인애플 스무디", "쿠키앤크림 파우더", "캐모마일 피라미드",
            "베티나르디 잉글리쉬 브렉퍼스트 티", "베오베 밀크쉐이크 파우더",
            "20oz 아이스컵", "16온스 라벤더 컵", "봉다리넷 테이크아웃 커피봉투",
            "도나우 쿠즈락 소떡소떡 8팩", "아임요 요거트 파우더 1kg",
            "까로망 딸기잼 + 요거트 파우더 2개 증정"
        ];
        var products = names.Select((name, index) => new Product(index + 1, name, 1000, "", seller,
            "기타", true, 0)).ToList();
        string[] Find(string query) => ProductSearch.Rank(products, query).Select(p => p.Name).ToArray();
        void Require(bool condition, string message)
        { if (!condition) throw new Exception("Search: " + message); }
        void Before(string query, string first, string later)
        {
            var result = Find(query);
            Require(result.Contains(first) && result.Contains(later) &&
                Array.IndexOf(result, first) < Array.IndexOf(result, later), query + ": " + first + " before " + later +
                " · results=" + string.Join(" | ", result));
        }
        Before("까로망 쵸코", "까로망 초코소스 2kg", "포모나 코코렛 파우더");
        Before("까로망 쵸코", "포모나 코코렛 파우더", "까로망 바닐라 소스");
        Before("쵸콜릿", "초콜릿 파우더", "기초코어 분석기");
        Require(new[] { "까로망 초코소스 2kg", "까로망 다크 초코렛 소스",
            "포모나 코코렛 파우더", "쵸코 쿠키" }.All(Find("쵸콜릿").Contains),
            "Chocolate variants and middle substring are all present");
        Before("토피넛 파우더", "아임요 토피넛 파우더", "땅콩 스프레드");
        Before("땅콩", "땅콩 스프레드", "아임요 토피넛 파우더");
        Before("바나나 파우더", "바나나 파우더", "바나나 분말");
        Before("바나나 파우더", "바나나 우유", "콘차우더");
        Before("요거트 파우더", "아임요 요거트 파우더 1kg", "까로망 딸기잼 + 요거트 파우더 2개 증정");
        Require(Find("바나나 파우더").Contains("딸기 파우더"), "Broad OR includes another powder flavor");
        Before("92 뚜껑", "PET 투명컵용 뚜껑 D92 1박스", "90mm 종이컵 뚜껑");
        Before("92 돔", "92파이 돔리드", "192파이 뚜껑");
        Before("92", "PET 투명컵용 뚜껑 D92 1박스", "192파이 뚜껑");
        Require(new[] { "블랙티 홍차", "얼그레이 티백", "잉글리쉬 브렉퍼스트 티" }
            .All(Find("홍차").Contains) && !Find("홍차").Contains("캐모마일 티") &&
            !Find("홍차").Contains("페퍼민트 티"), "Black tea excludes herbal teas");
        Require(Find("냉동과일").Contains("냉동 망고 1kg") &&
            !Find("냉동과일").Contains("냉동 츄러스") && !Find("냉동과일").Contains("망고 스무디") &&
            Find("냉동").Contains("냉동 츄러스"), "Frozen fruit intent stays distinct");
        Require(Find("1284g 연유").First() == "응오이사오 프엉남 연유 대체품 1.284kg",
            "Exact weight conversion matches without assuming unrelated volume equivalents");
        Require(Find("20온스 컵").Contains("20oz 아이스컵") &&
            Find("16oz 라벤더").Contains("16온스 라벤더 컵"), "Ounce spelling conversions");
        foreach (string query in new[] { "샤인머스켓", "사인머스캣", "파인에플", "쿠키엔",
            "케모마일", "브랙퍼스트", "밀크 쉐이크", "커피봉투", "소떡" })
            Require(Find(query).Length > 0, "Expected cafe query: " + query);
        products.AddRange(Enumerable.Range(1000, 160).Select(index => new Product(index,
            "검사용 파우더 " + index, 1000, "", seller, "파우더", true, 0)));
        var many = ProductSearch.Rank(products, "파우더").Select(p => p.Id).ToArray();
        Require(many.Length >= 160 && many.Length == many.Distinct().Count() &&
            many.SequenceEqual(ProductSearch.Rank(products, "파우더").Select(p => p.Id)),
            "All matching products retained once in deterministic order, without a result cap");
        Console.WriteLine($"SEARCH fixture: 까로망 쵸코={Find("까로망 쵸코").Length}, " +
            $"바나나 파우더={Find("바나나 파우더").Length}, 파우더={many.Length} (no cap)");
    }
}
