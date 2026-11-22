using System.Net;
using System.Net.Http;
using System.Text.Json;

internal static class TranslationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    internal static List<string> Split(string text, int limit)
    {
        var parts = new List<string>(); int start = 0;
        while (start < text.Length)
        {
            int end = Math.Min(start + limit, text.Length);
            if (end < text.Length) { int p = text.LastIndexOfAny(new char[] { ' ', (char)9, (char)13, (char)10 }, end - 1, end - start); if (p > start + limit / 2) end = p + 1; }
            parts.Add(text[start..end].Trim()); start = end;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        }
        return parts.Where(x => x.Length > 0).ToList();
    }

    internal static async Task<string> TranslateChunk(string text, string pair, CancellationToken token)
    {
        string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) + "&langpair=" + pair;
        using var response = await Http.GetAsync(url, token); response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = doc.RootElement;
        if (root.TryGetProperty("responseStatus", out var s) && s.ValueKind == JsonValueKind.Number && s.GetInt32() != 200)
            throw new InvalidOperationException(root.TryGetProperty("responseDetails", out var d) ? d.GetString() ?? "翻译服务返回错误" : "翻译服务返回错误");
        string value = WebUtility.HtmlDecode(root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "").Trim();
        if (value.Length == 0) throw new InvalidOperationException("翻译服务没有返回有效译文。");
        return value;
    }
}
