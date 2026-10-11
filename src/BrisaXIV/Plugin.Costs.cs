using System.Numerics;
using System.Text.Json;
using BrisaXIV.Core;
using Dalamud.Bindings.ImGui;

namespace BrisaXIV;

public sealed partial class Plugin
{
    private AIUsageLedger usageLedger = new();
    private int costPeriod;
    private long nextUsageRefresh;
    private AIUsageSnapshot? usageSnapshot;
    private bool ShowAICosts => queue.AIConfigured;
#if !BRISA_READ_ONLY
    private string billingAdminKey = "", billingProjectDraft = "", billingKeyIdDraft = "";
    private string billingProject = "", billingKeyId = "", savedBillingAdminKey = "";
    private string? billingError;
    private volatile OpenAICostReport? billingReport;
    private readonly OpenAICostsClient billingClient = new();
    private readonly CancellationTokenSource billingStop = new();
    private Task? billingTask;
    private long nextBillingQuery;
    private int priceSelection;
    private string newPriceModel = "";
#endif
    private void InitializeUsage(string root)
    {
        config.AIPrices ??= [AIPricing.Luna];
#if !BRISA_READ_ONLY
        usageLedger = new(Path.Combine(root, "uso-ia.jsonl"));
        billingProjectDraft = billingProject = config.BillingProjectId;
        billingKeyIdDraft = billingKeyId = config.BillingApiKeyId;
        try { billingAdminKey = savedBillingAdminKey = KeyProtection.Unprotect(config.EncryptedBillingAdminKey); }
        catch { billingError = "A credencial de custos não pôde ser lida. Informe-a novamente no plugin."; }
        try
        {
            var path = Path.Combine(root, "custos-ia.json");
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<OpenAICostCache>(File.ReadAllText(path));
                if (saved != null && saved.Matches(config.BillingProfileId)) billingReport = saved.Report;
                else billingError = "O relatório salvo é incompleto ou pertence a outra configuração. Faça uma nova consulta de custos.";
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { billingError = "O último relatório de custos não pôde ser lido. Faça uma nova consulta."; }
#endif
    }
    private (DateOnly First, int Days) CostRange()
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = new DateOnly(now.Year, now.Month, 1);
        return costPeriod switch
        {
            1 => (month.AddMonths(-1), DateTime.DaysInMonth(month.AddMonths(-1).Year, month.AddMonths(-1).Month)),
            2 => (now.AddDays(-29), 30),
            _ => (month, DateTime.DaysInMonth(month.Year, month.Month)),
        };
    }
    private static string Dollars(decimal? usd) => usd is { } value ? $"US$ {value:N4}" : "Não verificado";
    private void DrawAICosts()
    {
        var range = CostRange();
        if (usageSnapshot == null || Environment.TickCount64 >= nextUsageRefresh)
        { usageSnapshot = usageLedger.Snapshot(range.First, range.Days, config.AIPrices); nextUsageRefresh = Environment.TickCount64 + 1000; }
        var usage = usageSnapshot;
        var local = queue.Provider == TranslationProvider.Ollama;
        ImGui.TextWrapped(local ? $"Ollama local · {queue.ActiveModel} · sem cobrança de API externa"
            : $"OpenAI · {queue.ActiveModel} · valores em USD; dias em UTC");
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Período", ref costPeriod, "Este mês\0Mês anterior\0Últimos 30 dias\0")) { nextUsageRefresh = 0; usageSnapshot = null; return; }
#if !BRISA_READ_ONLY
        var report = billingReport;
        var reportMatches = !local && report != null && report.FirstDay == range.First && report.Days == range.Days
            && report.ProjectId == billingProject && report.ApiKeyId == billingKeyId;
        if (config.AutoRefreshBilling && !local && savedBillingAdminKey.Length > 0 && Environment.TickCount64 >= nextBillingQuery) QueryBilling(range.First, range.Days);
#endif
        if (ImGui.BeginTable("CostSummaryCards", 3, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn(); CostCard("Estimativa do Brisa", Dollars(usage.Total.EstimatedUsd), "Tokens informados × tabela de preços", Accent);
            ImGui.TableNextColumn();
#if !BRISA_READ_ONLY
            CostCard("Custo informado pela API", local ? "Sem API externa" : reportMatches && report!.HasAmounts ? Dollars(report.TotalUsd) : "Não verificado",
                reportMatches ? report!.Scope : "Consulta administrativa opcional", Mint);
#else
            CostCard("Custo informado pela API", "Não verificado", "DLL somente de leitura", Mint);
#endif
            ImGui.TableNextColumn(); CostCard("Cobertura gratuita", local ? "IA local" : "Não informada",
                local ? "Execução neste computador" : "Créditos e descontos exigem confirmação", Muted);
            ImGui.EndTable();
        }
        ImGui.TextWrapped($"{usage.Total.Requests:N0} chamadas · {usage.Total.Input:N0} tokens de entrada · {usage.Total.Cached:N0} em cache · {usage.Total.Output:N0} de saída");
        if (local && usage.Models.Any(x => x.Provider == "OpenAI")) ImGui.TextWrapped("O modelo atual é local. A estimativa do período inclui as chamadas OpenAI feitas anteriormente.");
        if (usage.Total.Pending + usage.Total.Unknown + usage.Total.Unpriced > 0)
            ImGui.TextColored(Amber, $"{usage.Total.Pending:N0} em andamento | {usage.Total.Unknown:N0} sem uso informado | {usage.Total.Unpriced:N0} sem tarifa. A estimativa pode estar incompleta.");
        if (usageLedger.Warning != null) ImGui.TextWrapped(usageLedger.Warning);
        BeginCard("CostChartCard");
        ImGui.TextUnformatted("Custo por dia");
        var estimates = usage.Days.Select(x => x.Total.EstimatedUsd).ToArray();
#if !BRISA_READ_ONLY
        DrawCostChart(range.First, estimates, reportMatches ? report!.Daily.Select(x => x.HasAmount ? (decimal?)x.Usd : null).ToArray() : null);
#else
        DrawCostChart(range.First, estimates, null);
#endif
        ImGui.TextColored(Accent, "Estimativa do Brisa"); ImGui.SameLine(); ImGui.TextColored(Mint, "Custo informado pela API da conta");
        ImGui.TextWrapped("As barras são separadas e não devem ser somadas. A conta/projeto pode incluir outros aplicativos. Valores ausentes são desconhecidos, não gratuidade.");
        EndCard();
        BeginCard("CostModels"); ImGui.TextUnformatted("Por modelo · uso registrado pelo Brisa");
        if (ImGui.BeginTable("UsageModelTable", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollX,
            new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X - CardPadding), 0)))
        {
            ImGui.TableSetupColumn("Modelo", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Chamadas", ImGuiTableColumnFlags.WidthFixed, 85);
            foreach (var title in new[] { "Entrada", "Cache", "Saída" }) ImGui.TableSetupColumn(title, ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Estimado USD", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            foreach (var model in usage.Models)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextWrapped(model.Model + (model.Provider == "Ollama" ? " · local" : ""));
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{model.Requests:N0}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{model.Input:N0}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{model.Cached:N0}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{model.Output:N0}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(Dollars(model.EstimatedUsd));
            }
            ImGui.EndTable();
        }
        if (usage.Models.Length == 0) ImGui.TextWrapped("Ainda não há chamadas de IA registradas neste período. Ler uma tradução do TXT não gera nova chamada.");
        ImGui.TextWrapped(usage.TrackingStarted is { } started
            ? $"Registro local desde {started.ToLocalTime():dd/MM/yyyy HH:mm}. Traduções antigas do TXT não permitem reconstruir cobranças passadas."
            : "O histórico começará na próxima chamada. O TXT existente não é reenviado para medir custos.");
        EndCard();
#if !BRISA_READ_ONLY
        if (!local)
        {
            BeginCard("ActualCharges"); ImGui.TextUnformatted("Conferir custos na OpenAI");
            ImGui.TextWrapped("A chave comum de tradução não confirma a fatura. Esta consulta usa uma credencial administrativa separada, protegida pelo Windows e enviada somente à API de custos.");
            if (ImGui.CollapsingHeader("Configurar consulta administrativa"))
            {
                ImGui.InputText("Credencial administrativa", ref billingAdminKey, 512, ImGuiInputTextFlags.Password);
                ImGui.InputText("ID do projeto (opcional)", ref billingProjectDraft, 200);
                ImGui.InputText("ID da chave (opcional, não é o segredo)", ref billingKeyIdDraft, 200);
                if (ImGui.Button("Salvar consulta de custos")) SaveBillingConfiguration();
                ImGui.SameLine();
                var busy = billingTask is { IsCompleted: false };
                ImGui.BeginDisabled(busy || savedBillingAdminKey.Length == 0 || Environment.TickCount64 < nextBillingQuery);
                if (ImGui.Button(busy ? "Consultando…" : "Consultar agora")) QueryBilling(range.First, range.Days);
                ImGui.EndDisabled();
                if (ImGui.Checkbox("Atualizar a cada 15 minutos enquanto esta página estiver aberta", ref config.AutoRefreshBilling)) PluginInterface.SavePluginConfig(config);
            }
            if (billingError != null) ImGui.TextWrapped(billingError);
            if (reportMatches)
            {
                ImGui.TextWrapped($"Última consulta: {report!.Fetched.ToLocalTime():dd/MM/yyyy HH:mm:ss}. Escopo: {report.Scope}.");
                ImGui.TextWrapped(report.HasAmounts && report.TotalUsd > 0 ? "Há custo informado pela OpenAI neste recorte."
                    : "A API ainda não informou custo positivo neste recorte. Isso não confirma gratuidade nem que a cobrança já foi consolidada.");
                foreach (var item in report.Items) ImGui.TextWrapped($"{item.Name} · {Dollars(item.Usd)}");
            }
            ImGui.TextWrapped("O valor da API é um lançamento de custo da conta, não uma confirmação de débito no cartão. Não inferimos cota gratuita subtraindo esse valor da estimativa do plugin.");
            EndCard(); DrawPricing();
        }
#endif
    }
    private static void CostCard(string label, string value, string note, Vector4 color)
    {
        BeginCard("Card" + label);
        ImGui.TextColored(Muted, label); ImGui.TextColored(color, value);
        ImGui.TextWrapped(note); EndCard();
    }
    private static void DrawCostChart(DateOnly firstDay, decimal?[] estimate, decimal?[]? confirmed)
    {
        var origin = ImGui.GetCursorScreenPos(); var width = Math.Max(180, ImGui.GetContentRegionAvail().X - CardPadding);
        const float height = 190, top = 24, bottom = 30;
        var values = estimate.Concat(confirmed ?? []).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var max = Math.Max(0.0001m, values.Length == 0 ? 0 : values.Max());
        var min = Math.Min(0, values.Length == 0 ? 0 : values.Min());
        var range = max - min; var plotHeight = height - top - bottom;
        float Y(decimal value) => origin.Y + top + (float)((max - value) / range) * plotHeight;
        var draw = ImGui.GetWindowDrawList(); var zero = Y(0);
        draw.AddLine(new(origin.X, zero), new(origin.X + width, zero), ImGui.GetColorU32(new Vector4(0.32f, 0.34f, 0.42f, 1)));
        draw.AddText(origin, ImGui.GetColorU32(Muted), $"pico {Dollars(max)} / dia");
        var step = width / estimate.Length;
        for (var i = 0; i < estimate.Length; i++)
        {
            var x = origin.X + i * step + 2; var barWidth = Math.Max(1, (step - 5) / (confirmed == null ? 1 : 2));
            void Bar(decimal? value, float left, Vector4 color)
            {
                if (value is not { } amount || amount == 0) return;
                var y = Y(amount);
                draw.AddRectFilled(new(left, Math.Min(y, zero)), new(left + barWidth, Math.Max(y, zero)), ImGui.GetColorU32(color), 2);
            }
            Bar(estimate[i], x, Accent);
            if (confirmed != null) Bar(confirmed[i], x + barWidth + 1, Mint);
        }
        draw.AddText(new(origin.X, origin.Y + height - 20), ImGui.GetColorU32(Muted), firstDay.ToString("dd/MM"));
        var last = firstDay.AddDays(estimate.Length - 1).ToString("dd/MM");
        draw.AddText(new(origin.X + width - ImGui.CalcTextSize(last).X, origin.Y + height - 20), ImGui.GetColorU32(Muted), last);
        ImGui.InvisibleButton("DailyCostGraph", new(width, height));
        if (ImGui.IsItemHovered())
        {
            var index = Math.Clamp((int)((ImGui.GetMousePos().X - origin.X) / step), 0, estimate.Length - 1);
            ImGui.BeginTooltip(); ImGui.TextUnformatted(firstDay.AddDays(index).ToString("dd/MM/yyyy") + " · UTC");
            ImGui.TextUnformatted("Brisa estimado: " + Dollars(estimate[index]));
            ImGui.TextUnformatted("API da conta: " + Dollars(confirmed?[index])); ImGui.EndTooltip();
        }
    }
#if !BRISA_READ_ONLY
    private void SaveBillingConfiguration()
    {
        try
        {
            config.EncryptedBillingAdminKey = KeyProtection.Protect(billingAdminKey);
            config.BillingProjectId = billingProjectDraft.Trim(); config.BillingApiKeyId = billingKeyIdDraft.Trim();
            config.BillingProfileId = Guid.NewGuid().ToString("N");
            PluginInterface.SavePluginConfig(config);
            savedBillingAdminKey = billingAdminKey.Trim(); billingProject = config.BillingProjectId; billingKeyId = config.BillingApiKeyId;
            billingReport = null; billingError = null; nextBillingQuery = 0;
        }
        catch { billingError = "Não foi possível proteger e salvar a credencial de custos."; }
    }
    private void QueryBilling(DateOnly first, int days)
    {
        if (billingTask is { IsCompleted: false } || Environment.TickCount64 < nextBillingQuery || savedBillingAdminKey.Length == 0) return;
        var key = savedBillingAdminKey; var project = billingProject; var keyId = billingKeyId; var profile = config.BillingProfileId;
        nextBillingQuery = Environment.TickCount64 + (config.AutoRefreshBilling ? 15 * 60000 : 60000);
        billingTask = Task.Run(async () =>
        {
            try
            {
                var report = await billingClient.FetchAsync(first, days, key, project, keyId, billingStop.Token).ConfigureAwait(false);
                // A save during the request must not relabel an earlier account's report.
                if (savedBillingAdminKey != key || billingProject != project || billingKeyId != keyId || config.BillingProfileId != profile) return;
                billingReport = report; billingError = null;
                var path = Path.Combine(Path.GetDirectoryName(file.Path)!, "custos-ia.json"); var temporary = path + ".tmp";
                try { File.WriteAllText(temporary, JsonSerializer.Serialize(new OpenAICostCache(profile, report))); File.Move(temporary, path, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { billingError = "Custos consultados, mas o relatório local não pôde ser salvo."; }
            }
            catch (OperationCanceledException) when (billingStop.IsCancellationRequested) { }
            catch (BillingException ex)
            {
                if (savedBillingAdminKey != key || config.BillingProfileId != profile) return;
                billingError = ex.Message;
                if (ex.RetryAfter is { } wait) nextBillingQuery = Math.Max(nextBillingQuery, Environment.TickCount64 + (long)wait.TotalMilliseconds);
            }
            catch { if (savedBillingAdminKey == key && config.BillingProfileId == profile) billingError = "Não foi possível concluir a consulta de custos. As traduções continuam e nenhum novo valor foi confirmado."; }
        });
    }
    private void DrawPricing()
    {
        BeginCard("PriceSettings");
        if (ImGui.CollapsingHeader("Tarifas usadas na estimativa"))
        {
            ImGui.TextWrapped("Luna padrão: US$ 0,10 entrada, US$ 0,01 cache, US$ 0,50 saída e US$ 0,125 escrita de cache por milhão de tokens. Verificado em 10/10/2026. Promoções e créditos da conta não são inferidos.");
            if (config.AIPrices.Count > 0)
            {
                priceSelection = Math.Clamp(priceSelection, 0, config.AIPrices.Count - 1);
                ImGui.Combo("Tarifa do modelo", ref priceSelection, string.Join('\0', config.AIPrices.Select(x => x.Model)) + '\0');
                var price = config.AIPrices[priceSelection];
                var input = (float)price.InputPerMillion; var cache = (float)price.CachedPerMillion;
                var output = (float)price.OutputPerMillion; var write = (float)price.CacheWritePerMillion;
                var changed = ImGui.InputFloat("Entrada / 1M", ref input, 0, 0, "%.6f");
                changed |= ImGui.InputFloat("Cache / 1M", ref cache, 0, 0, "%.6f");
                changed |= ImGui.InputFloat("Saída / 1M", ref output, 0, 0, "%.6f");
                changed |= ImGui.InputFloat("Escrita de cache / 1M", ref write, 0, 0, "%.6f");
                if (changed && float.IsFinite(input) && float.IsFinite(cache) && float.IsFinite(output) && float.IsFinite(write))
                {
                    config.AIPrices[priceSelection] = price with { InputPerMillion = (decimal)Math.Clamp(input, 0, 10000), CachedPerMillion = (decimal)Math.Clamp(cache, 0, 10000), OutputPerMillion = (decimal)Math.Clamp(output, 0, 10000), CacheWritePerMillion = (decimal)Math.Clamp(write, 0, 10000) };
                    PluginInterface.SavePluginConfig(config); nextUsageRefresh = 0;
                }
                if (!price.Configured)
                {
                    ImGui.TextColored(Amber, "Tarifa ainda não configurada. Preencha os valores e confirme.");
                    if (ImGui.Button("Confirmar tarifa deste modelo"))
                    { config.AIPrices[priceSelection] = config.AIPrices[priceSelection] with { Configured = true }; PluginInterface.SavePluginConfig(config); nextUsageRefresh = 0; }
                }
            }
            ImGui.InputText("Outro modelo", ref newPriceModel, 200);
            if (ImGui.Button("Adicionar tarifa") && !string.IsNullOrWhiteSpace(newPriceModel) && !newPriceModel.StartsWith("sk-", StringComparison.Ordinal)
                && !config.AIPrices.Any(x => x.Model == newPriceModel.Trim()))
            { config.AIPrices.Add(new(newPriceModel.Trim(), 0, 0, 0, 0, Configured: false)); priceSelection = config.AIPrices.Count - 1; PluginInterface.SavePluginConfig(config); nextUsageRefresh = 0; }
            ImGui.TextWrapped("A tabela atual recalcula o histórico estimado. Modelos ou níveis de serviço sem tarifa conhecida ficam sem estimativa. Tokens de cache já estão incluídos na entrada; não são somados duas vezes.");
        }
        EndCard();
    }
#endif
}
