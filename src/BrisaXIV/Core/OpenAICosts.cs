#if !BRISA_READ_ONLY
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrisaXIV.Core;

public sealed record ReportedCostDay(DateOnly Day, decimal Usd, bool HasAmount);
public sealed record ReportedCostItem(string Name, decimal Usd);
public sealed record OpenAICostReport(DateOnly FirstDay, int Days, DateTimeOffset Fetched,
    string ProjectId, string ApiKeyId, ReportedCostDay[] Daily, ReportedCostItem[] Items)
{
    public decimal TotalUsd => Daily.Sum(x => x.Usd);
    public bool HasAmounts => Daily.Any(x => x.HasAmount);
    public string Scope => ApiKeyId.Length > 0 ? "Chave selecionada na conta" : ProjectId.Length > 0 ? "Projeto selecionado na conta" : "Toda a organização / conta";
    public bool Valid => Days is >= 1 and <= 31 && FirstDay.DayNumber <= DateOnly.MaxValue.DayNumber - Days + 1 && ProjectId != null && ApiKeyId != null
        && Daily != null && Daily.Length == Days && Items != null
        && Daily.Select((day, index) => day != null && day.Day == FirstDay.AddDays(index)).All(x => x)
        && Items.All(x => x != null && x.Name != null);
}
public sealed class BillingException(string message, TimeSpan? retryAfter = null) : Exception(message)
{ public TimeSpan? RetryAfter { get; } = retryAfter; }
public sealed record OpenAICostCache(string ProfileId, OpenAICostReport Report)
{
    public bool Matches(string profileId) => !string.IsNullOrEmpty(profileId) && ProfileId == profileId && Report is { Valid: true };
}

// Only GETs the official Costs endpoint. The dedicated credential is never shared
// with Translator and redirects are disabled, including on authentication errors.
public sealed class OpenAICostsClient : IDisposable
{
    private readonly HttpClient client;
    public OpenAICostsClient(HttpMessageHandler? handler = null)
        => client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
    public async Task<OpenAICostReport> FetchAsync(DateOnly firstDay, int days, string adminKey, string projectId, string apiKeyId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(adminKey)) throw new BillingException("Informe a credencial administrativa de consulta de custos no próprio plugin.");
        if (days is < 1 or > 31) throw new BillingException("Escolha um período de até 31 dias.");
        projectId = projectId.Trim(); apiKeyId = apiKeyId.Trim();
        foreach (var id in new[] { projectId, apiKeyId })
            if (id.Length > 0 && (id.StartsWith("sk-", StringComparison.Ordinal) || !Regex.IsMatch(id, @"^[A-Za-z0-9_-]{1,200}$")))
                throw new BillingException("Os filtros usam IDs de projeto/chave, nunca a chave secreta de tradução.");
        var start = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = start.AddDays(days);
        if (end > DateTimeOffset.UtcNow) end = DateTimeOffset.UtcNow;
        if (end <= start) throw new BillingException("O período de custos ainda não começou.");
        var query = $"start_time={start.ToUnixTimeSeconds()}&end_time={end.ToUnixTimeSeconds()}&bucket_width=1d&limit=31&group_by%5B%5D=line_item";
        if (projectId.Length > 0) query += "&project_ids%5B%5D=" + Uri.EscapeDataString(projectId);
        if (apiKeyId.Length > 0) query += "&api_key_ids%5B%5D=" + Uri.EscapeDataString(apiKeyId);
        var totals = new Dictionary<DateOnly, decimal>(); var reported = new HashSet<DateOnly>();
        var items = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var seenPages = new HashSet<string>(StringComparer.Ordinal); string? page = null;
        for (var n = 0; n < 20; n++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/organization/costs?" + query
                + (page == null ? "" : "&page=" + Uri.EscapeDataString(page)));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminKey.Trim());
            using var response = await client.SendAsync(request, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 401 or 403) throw new BillingException("A consulta exige uma credencial administrativa com permissão de custos. A chave comum de tradução pode não ter esse acesso.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new BillingException("A consulta de custos atingiu o limite. As traduções continuam normalmente.", OpenAIRateLimiter.RetryAfter(response) ?? TimeSpan.FromMinutes(1));
            if (!response.IsSuccessStatusCode) throw new BillingException($"Não foi possível consultar os custos (HTTP {(int)response.StatusCode}). Nenhum valor novo foi confirmado.");
            try
            {
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
                var root = json.RootElement;
                foreach (var bucket in root.GetProperty("data").EnumerateArray())
                {
                    var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(bucket.GetProperty("start_time").GetInt64()).UtcDateTime);
                    if (day < firstDay || day >= firstDay.AddDays(days)) continue;
                    foreach (var result in bucket.GetProperty("results").EnumerateArray())
                    {
                        if (!result.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.Object
                            || !amount.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var usd)) continue;
                        if (!amount.TryGetProperty("currency", out var currency) || currency.GetString() != "usd") throw new BillingException("A API retornou valores em outra moeda. O painel não mistura esses valores com USD.");
                        totals[day] = totals.GetValueOrDefault(day) + usd; reported.Add(day);
                        var item = result.TryGetProperty("line_item", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "Outros lançamentos";
                        if (item.Length > 300) item = "Outros lançamentos";
                        items[item] = items.GetValueOrDefault(item) + usd;
                    }
                }
                if (!root.GetProperty("has_more").GetBoolean())
                    return new(firstDay, days, DateTimeOffset.UtcNow, projectId, apiKeyId,
                        Enumerable.Range(0, days).Select(i => new ReportedCostDay(firstDay.AddDays(i), totals.GetValueOrDefault(firstDay.AddDays(i)), reported.Contains(firstDay.AddDays(i)))).ToArray(),
                        items.Select(x => new ReportedCostItem(x.Key, x.Value)).OrderByDescending(x => x.Usd).ToArray());
                page = root.GetProperty("next_page").GetString();
                if (string.IsNullOrEmpty(page) || page.Length > 4096 || !seenPages.Add(page)) throw new BillingException("A API não concluiu a paginação de custos. Nenhum resultado parcial será apresentado como completo.");
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
            { throw new BillingException("A API retornou dados de custos incompletos ou fora do formato. O valor não foi confirmado."); }
        }
        throw new BillingException("A consulta ultrapassou o limite de páginas. Nenhum resultado parcial foi confirmado.");
    }
    public void Dispose() => client.Dispose();
}
#endif
