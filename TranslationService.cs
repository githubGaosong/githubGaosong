using System.Net;
using System.Net.Http;
using System.Text.Json;

internal static class TranslationService
{
    private const int ChunkLimit = 450;
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    internal static List<string> Split(string text, int limit)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (text.Length == 0) return new List<string>();

        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + limit, text.Length);
            if (end < text.Length)
            {
                int boundary = text.LastIndexOfAny(new[] { ' ', '\\t', '\\r', '\\n' }, end - 1, end - start);
                if (boundary > start + limit / 2)
                    end = boundary + 1;
            }

            // Never split a UTF-16 surrogate pair between two requests.
            if (end < text.Length && end > start &&
                char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]))
                end--;

            if (end <= start) end = Math.Min(start + limit + 1, text.Length);
            string chunk = text[start..end].Trim();
            if (chunk.Length > 0) chunks.Add(chunk);

            start = end;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        }

        return chunks;
    }

    internal static async Task<string> TranslateChunk(string text, string pair, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(pair);

        string url = "https://api.mymemory.translated.net/get?q=" +
            Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString(pair);

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            string reason = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "翻译请求过于频繁，请稍后重试。",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "翻译服务响应超时，请重试。",
                _ when (int)response.StatusCode >= 500 => "翻译服务暂时不可用，请稍后重试。",
                _ => $"翻译请求失败（HTTP {(int)response.StatusCode}）。"
            };
            throw new HttpRequestException(reason, null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("responseStatus", out JsonElement status) &&
            status.ValueKind == JsonValueKind.Number && status.GetInt32() != 200)
        {
            string details = root.TryGetProperty("responseDetails", out JsonElement detail) &&
                detail.ValueKind == JsonValueKind.String
                    ? detail.GetString() ?? "翻译服务返回错误。"
                    : "翻译服务返回错误。";
            throw new InvalidOperationException(details);
        }

        if (!root.TryGetProperty("responseData", out JsonElement data) ||
            !data.TryGetProperty("translatedText", out JsonElement translated) ||
            translated.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("翻译服务返回的数据格式不正确。");

        string result = WebUtility.HtmlDecode(translated.GetString() ?? string.Empty).Trim();
        if (result.Length == 0)
            throw new InvalidOperationException("翻译服务没有返回有效译文。");

        return result;
    }
}
