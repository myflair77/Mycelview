using System.Text;

namespace PinShelf;

public static class SearchQuery
{
    private enum Kind { Term, And, Or, Not, Left, Right, End }
    private sealed record Token(Kind Kind, string Text, int Position);
    public sealed record Plan(Func<string, bool> Match, Func<string, string, bool> MatchCached, string Sql, List<string> Parameters);
    private sealed record Node(Func<string, bool> Match, Func<string, string, bool> MatchCached, string Sql);
    public static string Initials(string text)
    {
        const string initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        return new string(text.Select(c => c >= '\uAC00' && c <= '\uD7A3' ? initials[(c - '\uAC00') / 588] : c).ToArray());
    }
    public static Func<string, bool> Parse(string input) => Compile(input).Match;
    public static Plan Compile(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new FormatException("검색어를 입력하세요.");
        if (input.Length > 4096) throw new FormatException("검색식은 4,096자 이내로 입력하세요.");
        var parameters = new List<string>();
        var tokens = new List<Token>();
        for (int i = 0; i < input.Length;)
        {
            if (char.IsWhiteSpace(input[i])) { i++; continue; }
            int start = i; char c = input[i++];
            var symbol = c switch { '&' => Kind.And, '|' => Kind.Or, '!' => Kind.Not, '(' => Kind.Left, ')' => Kind.Right, _ => Kind.Term };
            if (symbol != Kind.Term) { tokens.Add(new(symbol, c.ToString(), start)); continue; }
            if (c == '"')
            {
                var text = new StringBuilder();
                while (i < input.Length && input[i] != '"') text.Append(input[i++]);
                if (i == input.Length) throw new FormatException($"{start + 1}번째 문자의 따옴표를 닫아 주세요.");
                i++;
                if (text.Length == 0) throw new FormatException("따옴표 안에 검색어를 입력하세요.");
                tokens.Add(new(Kind.Term, text.ToString(), start)); continue;
            }
            while (i < input.Length && !char.IsWhiteSpace(input[i]) && !"&|!()\"".Contains(input[i])) i++;
            var word = input[start..i];
            var kind = word.ToUpperInvariant() switch { "AND" => Kind.And, "OR" => Kind.Or, "NOT" => Kind.Not, _ => Kind.Term };
            tokens.Add(new(kind, word, start));
        }
        tokens.Add(new(Kind.End, "", input.Length));
        int index = 0, depth = 0;
        Node Atom()
        {
            if (++depth > 64) throw new FormatException("괄호 또는 NOT 중첩은 64단계 이내로 입력하세요.");
            try
            {
                var token = tokens[index++];
                if (token.Kind == Kind.Not) { var operand = Atom(); return new(s => !operand.Match(s), (s, i) => !operand.MatchCached(s, i), "(NOT " + operand.Sql + ")"); }
                if (token.Kind == Kind.Left)
                {
                    var inner = Or();
                    if (tokens[index].Kind != Kind.Right) throw new FormatException("닫는 괄호 ')'가 필요합니다.");
                    index++; return inner;
                }
                if (token.Kind != Kind.Term) throw new FormatException($"{token.Position + 1}번째 위치에 검색어 또는 여는 괄호가 필요합니다.");
                bool initial = token.Text.Any(c => c >= 'ㄱ' && c <= 'ㅎ');
                var term = (initial ? Initials(token.Text) : token.Text).ToUpperInvariant();
                parameters.Add("%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
                return new(s => (initial ? Initials(s) : s).Contains(term, StringComparison.OrdinalIgnoreCase),
                    (s, i) => (initial ? i : s).Contains(term, StringComparison.Ordinal),
                    $"({(initial ? "initials" : "search")} LIKE ?{parameters.Count} ESCAPE '\\')");
            }
            finally { depth--; }
        }
        Node And()
        {
            var items = new List<Node> { Atom() };
            while (tokens[index].Kind is Kind.And or Kind.Term or Kind.Left or Kind.Not)
            { if (tokens[index].Kind == Kind.And) index++; items.Add(Atom()); }
            if (items.Count == 1) return items[0];
            return new(s => items.All(node => node.Match(s)), (s, i) => { foreach (var node in items) if (!node.MatchCached(s, i)) return false; return true; }, "(" + string.Join(" AND ", items.Select(node => node.Sql)) + ")");
        }
        Node Or()
        {
            var items = new List<Node> { And() };
            while (tokens[index].Kind == Kind.Or) { index++; items.Add(And()); }
            if (items.Count == 1) return items[0];
            return new(s => items.Any(node => node.Match(s)), (s, i) => { foreach (var node in items) if (node.MatchCached(s, i)) return true; return false; }, "(" + string.Join(" OR ", items.Select(node => node.Sql)) + ")");
        }
        var result = Or();
        if (tokens[index].Kind != Kind.End) throw new FormatException($"{tokens[index].Position + 1}번째 위치의 괄호 또는 연산자를 확인하세요.");
        return new(result.Match, result.MatchCached, result.Sql, parameters);
    }
}


