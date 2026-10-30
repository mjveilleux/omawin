using System.Globalization;

namespace SystemMenu;

// The launcher's calculator: a query that reads as arithmetic ("12*4.5", "2^10", "sqrt(2)*pi",
// "200*15%") gets its value as the top row; Enter copies it. A small recursive-descent parser,
// never a script engine: numbers, + - * / ^ (or ** ), % (modulo, or percent at the end of a
// term), parentheses, x × ÷, the constants pi and e, and sqrt abs round floor ceil ln log
// sin cos tan (radians). Anything else (a letter that isn't one of those) is "not a sum", and
// the scan stops at the first such character, so typing an app's name costs next to nothing.
public static class Calc
{
    // The value, when the query is a sum worth showing: it has an operator or a function, not
    // just a number ("42", "7zip" and "2024" are searches).
    public static bool TryEvaluate(string query, out double value)
    {
        value = 0;
        if (query.Length == 0 || query.Length > 200) return false;
        char first = query[0];
        if (!(char.IsDigit(first) || first is '(' or '-' or '.' or '√' || char.IsLetter(first))) return false;
        if (!Tokenize(query, out var tokens)) return false;
        if (!tokens.Any(t => t.Kind is 'o' or 'f') && !(tokens.Count > 1 && tokens.Any(t => t.Kind == 'c'))) return false; // "2pi" counts
        try
        {
            var p = new Parser(tokens);
            value = p.Expression();
            if (p.At < tokens.Count) return false;
        }
        catch (FormatException) { return false; }
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // As shown and copied: up to 12 significant digits, no thousands separators.
    public static string Format(double value)
    {
        if (Math.Abs(value) < 1e-12) value = 0;
        if (Math.Abs(value) < 1e15 && value == Math.Round(value)) return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("G12", CultureInfo.InvariantCulture);
    }

    // n: number, o: operator (+ - * / ^ %), f: function, c: constant, ( and ).
    record struct Token(char Kind, string Text, double Number = 0);

    static readonly HashSet<string> Functions = ["sqrt", "abs", "round", "floor", "ceil", "ln", "log", "sin", "cos", "tan"];

    static bool Tokenize(string s, out List<Token> tokens)
    {
        tokens = [];
        for (int i = 0; i < s.Length;)
        {
            char c = s[i];
            if (c == ' ') { i++; continue; }
            if (char.IsDigit(c) || c == '.')
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                // 1e3, 2.5e-4 (an e that isn't followed by a digit is the constant)
                if (i + 1 < s.Length && s[i] is 'e' or 'E' && (char.IsDigit(s[i + 1]) || (s[i + 1] is '+' or '-' && i + 2 < s.Length && char.IsDigit(s[i + 2]))))
                {
                    i += 2;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                if (!double.TryParse(s[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return false;
                tokens.Add(new('n', s[start..i], n));
                continue;
            }
            if (char.IsAsciiLetterLower(c) || char.IsAsciiLetterUpper(c))
            {
                int start = i;
                while (i < s.Length && char.IsAsciiLetter(s[i])) i++;
                var word = s[start..i].ToLowerInvariant();
                if (word == "x") tokens.Add(new('o', "*"));
                else if (word is "pi" or "e") tokens.Add(new('c', word, word == "pi" ? Math.PI : Math.E));
                else if (Functions.Contains(word)) tokens.Add(new('f', word));
                else return false;
                continue;
            }
            if (c == '*' && i + 1 < s.Length && s[i + 1] == '*') { tokens.Add(new('o', "^")); i += 2; continue; }
            switch (c)
            {
                case '+' or '-' or '*' or '/' or '^' or '%': tokens.Add(new('o', c.ToString())); break;
                case '×': tokens.Add(new('o', "*")); break;
                case '÷': tokens.Add(new('o', "/")); break;
                case '√': tokens.Add(new('f', "sqrt")); break;
                case '(' or ')': tokens.Add(new(c, c.ToString())); break;
                default: return false;
            }
            i++;
        }
        return tokens.Count > 0;
    }

    // expression := term (('+' | '-') term)*
    // term       := unary (('*' | '/' | '%') unary | power-without-operator)*   "2pi", "3(4+1)"
    // unary      := ('-' | '+') unary | power                                 -2^2 = -4
    // power      := postfix ('^' unary)?                                      right to left
    // postfix    := primary '%'?                                              percent: "15%" = 0.15
    // primary    := number | constant | function primary | '(' expression ')'
    sealed class Parser(List<Token> tokens)
    {
        public int At;
        Token? Peek => At < tokens.Count ? tokens[At] : null;
        bool IsOp(string op) => Peek is { Kind: 'o' } t && t.Text == op;

        public double Expression()
        {
            double v = Term();
            while (IsOp("+") || IsOp("-"))
            {
                bool plus = tokens[At++].Text == "+";
                double r = Term();
                v = plus ? v + r : v - r;
            }
            return v;
        }

        double Term()
        {
            double v = Unary();
            while (true)
            {
                if (IsOp("*")) { At++; v *= Unary(); }
                else if (IsOp("/")) { At++; v /= Unary(); }
                else if (IsOp("%")) { At++; double r = Unary(); v = v - r * Math.Floor(v / r); }
                else if (Peek is { Kind: 'n' or 'c' or 'f' or '(' }) v *= Power(); // implicit: 2pi, 3(4+1)
                else return v;
            }
        }

        double Power()
        {
            double v = Postfix();
            if (IsOp("^")) { At++; return Math.Pow(v, Unary()); }
            return v;
        }

        double Unary()
        {
            if (IsOp("-")) { At++; return -Unary(); }
            if (IsOp("+")) { At++; return Unary(); }
            return Power();
        }

        double Postfix()
        {
            double v = Primary();
            // A % with nothing after it, or an operator or ) after it, is percent; else modulo.
            if (IsOp("%") && (At + 1 >= tokens.Count || tokens[At + 1].Kind is 'o' or ')')) { At++; v /= 100; }
            return v;
        }

        double Primary()
        {
            var t = Peek ?? throw new FormatException();
            At++;
            switch (t.Kind)
            {
                case 'n' or 'c': return t.Number;
                case '(':
                    double v = Expression();
                    if (Peek is not { Kind: ')' }) { if (Peek == null) return v; throw new FormatException(); } // a missing ) at the end is fine while typing
                    At++;
                    return v;
                case 'f':
                    double a = Peek is { Kind: '(' } ? Primary() : Unary();
                    return t.Text switch
                    {
                        "sqrt" => Math.Sqrt(a), "abs" => Math.Abs(a), "round" => Math.Round(a, MidpointRounding.AwayFromZero),
                        "floor" => Math.Floor(a), "ceil" => Math.Ceiling(a), "ln" => Math.Log(a), "log" => Math.Log10(a),
                        "sin" => Math.Sin(a), "cos" => Math.Cos(a), _ => Math.Tan(a),
                    };
                default: throw new FormatException();
            }
        }
    }
}
