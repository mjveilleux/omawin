namespace SystemMenu;

// Fuzzy matching for the launcher: fzy's scoring (github.com/jhawthorn/fzy), the letters of
// the query in order anywhere in the text, scored so a match at word starts, capitals and in
// runs beats a scattered one ("pbi": Power BI, not IDLE (Python ...)). It also returns which
// characters matched, for highlighting. A query with spaces matches word by word, in any
// order ("bi power"). When the letters don't line up, one typo is allowed per word
// ("termnial", "teasm"), scored below any real match.
public static class Fuzzy
{
    const double GapLeading = -0.005, GapTrailing = -0.005, GapInner = -0.01;
    const double Consecutive = 1.0, AfterSlash = 0.9, AfterWord = 0.8, Capital = 0.7, AfterDot = 0.6;
    public const double None = double.NegativeInfinity;

    // The score of query in text (None if it isn't there), and the matched positions.
    public static double Match(string text, string query, List<int>? positions = null)
    {
        double total = 0;
        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var found = new List<int>();
            double s = Word(text, word, found);
            // A weak, scattered match ("teasm" in "Create React App Sample") loses to the word
            // it's a typo of ("Teams").
            if (s < 0.6 * word.Length)
            {
                var typoAt = new List<int>();
                double t = Typo(text, word, typoAt);
                if (t > s) { s = t; found = typoAt; }
            }
            if (s == None) return None;
            total += s;
            positions?.AddRange(found);
        }
        return total;
    }

    static double Bonus(string text, int j)
    {
        if (j == 0) return AfterSlash;
        char prev = text[j - 1], c = text[j];
        if (prev is '/' or '\\') return AfterSlash;
        if (prev is ' ' or '-' or '_' or '(' or ':') return AfterWord;
        if (prev == '.') return AfterDot;
        if (char.IsLower(prev) && char.IsUpper(c)) return Capital;
        if (char.IsLetter(prev) && char.IsDigit(c)) return AfterWord;
        return 0;
    }

    // fzy: D[i,j] = best score with query[i] matched at text[j]; M[i,j] = best score with
    // query[..i] matched somewhere in text[..j].
    static double Word(string text, string query, List<int> positions)
    {
        int n = query.Length, m = text.Length;
        if (n == 0) return 0;
        if (n > m || m > 256) return None;
        if (string.Equals(text, query, StringComparison.OrdinalIgnoreCase))
        {
            for (int k = 0; k < n; k++) positions.Add(k);
            return 1000; // the whole thing, exactly
        }
        var bonus = new double[m];
        for (int j = 0; j < m; j++) bonus[j] = Bonus(text, j);
        var D = new double[n, m];
        var M = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            double prev = None;
            double gap = i == n - 1 ? GapTrailing : GapInner;
            char q = char.ToLowerInvariant(query[i]);
            for (int j = 0; j < m; j++)
            {
                if (q == char.ToLowerInvariant(text[j]))
                {
                    double s = None;
                    if (i == 0) s = j * GapLeading + bonus[j];
                    else if (j > 0) s = Math.Max(M[i - 1, j - 1] + bonus[j], D[i - 1, j - 1] + Consecutive);
                    D[i, j] = s;
                    M[i, j] = prev = Math.Max(s, prev + gap);
                }
                else
                {
                    D[i, j] = None;
                    M[i, j] = prev = prev + gap;
                }
            }
        }
        double score = M[n - 1, m - 1];
        if (score == None || double.IsNaN(score)) return None;
        // Which characters: walk back through the best path.
        var found = new int[n];
        bool mustMatch = false;
        int jj = m - 1;
        for (int i = n - 1; i >= 0; i--)
        {
            for (; jj >= 0; jj--)
            {
                if (D[i, jj] != None && (mustMatch || D[i, jj] == M[i, jj]))
                {
                    mustMatch = i > 0 && jj > 0 && M[i, jj] == D[i - 1, jj - 1] + Consecutive;
                    found[i] = jj--;
                    break;
                }
            }
        }
        positions.AddRange(found);
        // Starting at a word ("Notepad" for "ntpd") beats a run in the middle of one
        // ("Defragment and Optimize Drives").
        if (bonus[found[0]] >= AfterWord) score += 0.6;
        return score;
    }

    // One typo (two for long words): a letter wrong, missing, extra or swapped, against the
    // start of a word in the text. Scored low, so real matches always come first.
    static double Typo(string text, string query, List<int> positions)
    {
        if (query.Length < 4) return None;
        int allowed = query.Length >= 8 ? 2 : 1;
        double best = None;
        for (int start = 0; start < text.Length; start++)
        {
            if (start > 0 && Bonus(text, start) < AfterWord) continue; // word starts only
            for (int len = Math.Max(1, query.Length - allowed); len <= query.Length + allowed && start + len <= text.Length; len++)
            {
                int d = Distance(query.ToLowerInvariant(), text.Substring(start, len).ToLowerInvariant());
                if (d <= allowed)
                {
                    double s = 0.6 * query.Length - 1.5 * d - 0.01 * start;
                    if (s > best)
                    {
                        best = s;
                        positions.Clear();
                        for (int k = 0; k < len; k++) positions.Add(start + k);
                    }
                }
            }
        }
        return best;
    }

    // Optimal string alignment distance: insertions, deletions, substitutions, adjacent swaps.
    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}
