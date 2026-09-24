using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CafeOrder;

// Pure, local ranking. The index is rebuilt from the current in-memory catalog after edits/imports.
internal static class ProductSearch
{
    private static readonly Regex Words = new(@"[\p{L}\p{Nd}]+(?:\.[0-9]+)?", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"(?<![0-9])([0-9]+)(?![0-9])", RegexOptions.Compiled);
    private static readonly Regex Quantity = new(@"(?<number>[0-9]+(?:\.[0-9]+)?)(?<unit>kg|g|ml|l|oz|온스|온즈|mm|파이)", RegexOptions.Compiled);
    private static readonly string[] Fruits = ["망고", "딸기", "블루베리", "복숭아", "바나나", "파인애플", "자몽", "레몬", "사과", "체리", "라즈베리"];
    private static readonly string[] Common = ["파우더", "분말", "가루", "개", "개입", "박스", "box", "세트", "상품", "카페", "대용량", "1개"];
    private static readonly string[] NotBrands = ["바나나", "딸기", "망고", "복숭아", "초코", "초콜릿", "냉동", "연유", "커피", "원두", "아이스컵", "테이크아웃"];
    private static readonly string[] KnownBrands = ["까로망", "포모나", "아임요", "베오베", "베버시티", "스위트컵", "베티나르디", "타코", "서울우유", "cj메티에", "네이쳐티", "오모마켓", "봉다리넷", "도나우", "아이팩피앤디", "바리스타퀸", "꽃샘", "메가카페", "에빠니", "동서", "셀플러스", "오양식품", "삼립", "비나밀크"];
    private static readonly string[][] Equivalent =
    [
        ["초콜릿", "초콜렛", "초코렛", "초코", "쵸코", "쵸콜릿", "코코렛"],
        ["샤인머스켓", "샤인머스캣"], ["파인애플", "파인에플"], ["쿠키앤", "쿠키엔"],
        ["캐모마일", "케모마일", "카모마일"], ["브렉퍼스트", "브랙퍼스트"],
        ["밀크쉐이크", "밀크셰이크", "밀크쉐잌"],
        ["파우더", "분말", "가루"], ["뚜껑", "리드", "lid"],
        ["빨대", "스트로우", "straw"], ["블랙티", "블렉티"],
        ["20온스", "20온즈", "20oz"], ["16온스", "16온즈", "16oz"]
    ];
    private static readonly Dictionary<string, string[]> Related = new(StringComparer.Ordinal)
    {
        ["토피넛"] = ["피넛", "땅콩", "peanut"], ["피넛"] = ["토피넛", "땅콩", "peanut"],
        ["땅콩"] = ["피넛", "peanut", "토피넛"], ["peanut"] = ["땅콩", "피넛", "토피넛"],
        ["홍차"] = ["블랙티", "블렉티", "얼그레이", "잉글리쉬브렉퍼스트"],
        ["블랙티"] = ["홍차", "얼그레이", "잉글리쉬브렉퍼스트"],
        ["얼그레이"] = ["홍차", "블랙티", "블렉티", "잉글리쉬브렉퍼스트"],
        ["연유"] = ["응오이사오", "프엉남"], ["응오이사오"] = ["연유"],
        ["커피봉투"] = ["테이크아웃봉투", "비닐캐리어"],
        ["테이크아웃봉투"] = ["커피봉투", "비닐캐리어"],
        ["냉동과일"] = ["냉동망고", "냉동딸기", "냉동블루베리", "냉동복숭아", "냉동바나나"]
    };

    internal static IReadOnlyList<Product> Rank(IEnumerable<Product> source, string query)
    {
        var catalog = source.Where(p => p.IsActive).DistinctBy(p => p.Id).ToArray();
        string raw = Normalize(query);
        if (raw.Length == 0) return CatalogOrder(catalog).ToArray();
        string[] terms = Tokenize(raw);
        if (terms.Length == 0) return [];
        string compactQuery = Compact(raw);
        var brands = BrandWords(catalog);
        var weights = terms.Distinct(StringComparer.Ordinal)
            .ToDictionary(term => term, term => Specificity(term, catalog), StringComparer.Ordinal);
        string? queryBrand = brands.FirstOrDefault(brand => compactQuery.Contains(brand, StringComparison.Ordinal));
        var ranked = new List<(Product Product, int Group, double Score)>();
        foreach (var product in catalog)
        {
            string name = Normalize(product.Name);
            string compact = Compact(name);
            if (compact.Length == 0) continue;
            int promotionMark = name.IndexOf('+');
            string mainName = promotionMark >= 0 &&
                (name.Contains("증정", StringComparison.Ordinal) || name.Contains("사은품", StringComparison.Ordinal))
                ? name[..promotionMark] : name;
            if (compactQuery == "냉동과일" && !compact.Contains("냉동과일", StringComparison.Ordinal) &&
                !(compact.Contains("냉동", StringComparison.Ordinal) && Fruits.Any(compact.Contains))) continue;
            string[] nameWords = Tokenize(name);
            bool phrase = Compact(mainName).Contains(compactQuery, StringComparison.Ordinal) &&
                terms.All(term => Direct(mainName, term));
            int direct = terms.Count(term => Direct(name, term));
            bool allDirect = direct == terms.Length;
            int expanded = 0;
            double score = 0;
            foreach (var term in terms)
            {
                double weight = weights[term];
                if (Direct(name, term))
                {
                    score += (Direct(mainName, term) ? 10 : 2) * weight;
                    continue;
                }
                if (EquivalentTerms(term).Any(alias => WordAliasMatch(nameWords, alias)) ||
                    EquivalentQuantity(term, name) ||
                    !brands.Contains(term) && TypoMatch(term, nameWords))
                { expanded++; score += 7 * weight; continue; }
                double broad = BroadStrength(term, name, nameWords);
                score += broad * weight;
            }
            int group = allDirect ? 1 : direct + expanded == terms.Length ? 2 : 3;
            if (group == 3 && score <= 0) continue;
            if (phrase) score += 30;
            if (queryBrand != null)
            {
                bool sameBrand = compact.Contains(queryBrand, StringComparison.Ordinal);
                if (sameBrand && terms.Length > 1 && (direct + expanded > 1)) score += 8;
                else if (sameBrand && terms.Length > 1 && direct + expanded == 1) score -= 14;
                else if (!sameBrand && terms.Length > 1) score -= 2;
            }
            if (HasDifferentSpec(raw, name)) score -= 12;
            if (group == 3) score = Math.Max(0.01, score);
            ranked.Add((product, group, score));
        }
        var korean = StringComparer.Create(new CultureInfo("ko-KR"), false);
        return ranked.OrderBy(item => item.Group).ThenByDescending(item => item.Score)
            .ThenBy(item => item.Product.Name, korean).ThenBy(item => item.Product.Id)
            .Select(item => item.Product).ToArray();
    }

    private static IOrderedEnumerable<Product> CatalogOrder(IEnumerable<Product> products) => products
        .OrderBy(p => Array.IndexOf(SampleData.Categories, p.Category))
        .ThenBy(p => p.Name, StringComparer.Create(new CultureInfo("ko-KR"), false))
        .ThenBy(p => p.Id);

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
    private static string[] Tokenize(string text) => Words.Matches(text).Select(match => match.Value).ToArray();
    private static bool Direct(string name, string term) => term.All(char.IsAsciiDigit)
        ? Number.Matches(name).Any(match => match.Value == term)
        : Compact(name).Contains(Compact(term), StringComparison.Ordinal);
    private static bool WordStarts(IEnumerable<string> words, string term) => words.Any(word => word.StartsWith(term, StringComparison.Ordinal));
    private static bool WordAliasMatch(IEnumerable<string> words, string alias) =>
        WordStarts(words, alias) || (alias is "리드" or "lid" or "파우더" or "분말" or "가루" or "스트로우") &&
        words.Any(word => word.EndsWith(alias, StringComparison.Ordinal));
    private static IEnumerable<string> EquivalentTerms(string term) => Equivalent
        .Where(group => group.Contains(term, StringComparer.Ordinal))
        .SelectMany(group => group).Where(alias => alias != term).Distinct(StringComparer.Ordinal);
    private static bool EquivalentQuantity(string term, string name)
    {
        var query = Quantity.Match(Compact(term));
        if (!query.Success || !decimal.TryParse(query.Groups["number"].Value,
            NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return false;
        string unit = query.Groups["unit"].Value;
        foreach (Match item in Quantity.Matches(Compact(name)))
        {
            if (!decimal.TryParse(item.Groups["number"].Value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var other)) continue;
            string otherUnit = item.Groups["unit"].Value;
            if (unit == otherUnit && number == other) return true;
            if (unit == "kg" && otherUnit == "g" && number * 1000 == other ||
                unit == "g" && otherUnit == "kg" && number == other * 1000 ||
                unit == "l" && otherUnit == "ml" && number * 1000 == other ||
                unit == "ml" && otherUnit == "l" && number == other * 1000 ||
                unit is "온스" or "온즈" && otherUnit == "oz" && number == other ||
                unit == "oz" && otherUnit is "온스" or "온즈" && number == other ||
                unit is "mm" or "파이" && otherUnit is "mm" or "파이" && number == other)
                return true;
        }
        return false;
    }
    private static bool TypoMatch(string term, IEnumerable<string> words)
    {
        if (term.Length < 3 || Common.Contains(term)) return false;
        int distance = term.Length >= 7 ? 2 : 1;
        return words.Any(word =>
        {
            if (word.Length < term.Length - 1) return false;
            string prefix = word[..Math.Min(word.Length, term.Length)];
            return EditDistance(term, prefix, distance) <= distance;
        });
    }
    private static int EditDistance(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) > limit) return limit + 1;
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int i = 1; i <= left.Length; i++)
        {
            int[] next = new int[right.Length + 1]; next[0] = i;
            for (int j = 1; j <= right.Length; j++)
                next[j] = Math.Min(Math.Min(next[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            if (next.Min() > limit) return limit + 1;
            previous = next;
        }
        return previous[^1];
    }
    private static double BroadStrength(string term, string name, string[] words)
    {
        double strength = 0;
        if (Related.TryGetValue(term, out var related) && related.Any(alias =>
            WordStarts(words, alias) || Compact(name).Contains(alias, StringComparison.Ordinal))) strength = 5;
        if (term == "냉동과일" && name.Contains("냉동", StringComparison.Ordinal) && Fruits.Any(name.Contains)) strength = 6;
        foreach (var alias in EquivalentTerms(term))
            if (Compact(name).Contains(alias, StringComparison.Ordinal)) strength = Math.Max(strength, 4);
        if (term.All(char.IsAsciiDigit) && Compact(name).Contains(term, StringComparison.Ordinal))
            strength = Math.Max(strength, 2);
        if (term.Length >= 2)
        {
            int overlap = 0;
            foreach (var word in words)
                for (int length = Math.Min(term.Length, word.Length); length >= 2; length--)
                {
                    if (Enumerable.Range(0, term.Length - length + 1).Any(start =>
                        word.Contains(term.Substring(start, length), StringComparison.Ordinal)))
                    { overlap = Math.Max(overlap, length); break; }
                }
            if (overlap >= 2) strength = Math.Max(strength, overlap == 2 ? 0.5 : Math.Min(2.5, overlap * 0.65));
        }
        return strength;
    }
    private static double Specificity(string term, IReadOnlyList<Product> catalog)
    {
        if (Common.Contains(term)) return 0.7;
        int frequency = catalog.Count(p => Direct(Normalize(p.Name), term));
        return Math.Clamp(1 + Math.Log((catalog.Count + 1d) / (frequency + 1d)) * 0.35, 1, 2.5);
    }
    private static HashSet<string> BrandWords(IReadOnlyList<Product> catalog)
    {
        var brands = KnownBrands.ToHashSet(StringComparer.Ordinal);
        foreach (var group in catalog.Select(p => Tokenize(Normalize(p.Name)).FirstOrDefault())
            .Where(word => word is { Length: >= 2 } && !Common.Contains(word) &&
                !NotBrands.Contains(word) && !Fruits.Contains(word))
            .GroupBy(word => word!))
            if (group.Count() >= 2) brands.Add(group.Key);
        return brands;
    }
    private static bool HasDifferentSpec(string query, string name)
    {
        var querySpecs = Quantity.Matches(Compact(query)).Select(match => (Number: match.Groups["number"].Value,
            Unit: match.Groups["unit"].Value)).ToArray();
        var nameSpecs = Quantity.Matches(Compact(name)).Select(match => (Number: match.Groups["number"].Value,
            Unit: match.Groups["unit"].Value)).ToArray();
        return querySpecs.Any(spec => nameSpecs.Any(other => spec.Unit == other.Unit && spec.Number != other.Number));
    }
}
