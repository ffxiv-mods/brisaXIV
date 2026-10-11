using System.Numerics;
using BrisaXIV.Core;
using Dalamud.Bindings.ImGui;

namespace BrisaXIV;

public sealed partial class Plugin
{
    private bool selectProgressTab;
    private ProgressSnapshot? progressSnapshot;
    private long nextProgressRefresh;
    private int progressFilter;
    private string progressSearch = "";
    private ProgressText? selectedProgressText;
    private ProgressRequest? selectedProgressRequest;

    private static string StageLabel(TranslationStage stage) => stage switch
    {
        TranslationStage.Queued => "Na fila", TranslationStage.Preparing => "Preparando lote",
        TranslationStage.WaitingCapacity => "Aguardando vaga", TranslationStage.WaitingLimit => "Aguardando limite",
        TranslationStage.AwaitingResponse => "Enviado / aguardando resposta", TranslationStage.Validating => "Validando resposta",
        TranslationStage.Retrying => "Nova tentativa", TranslationStage.Saving => "Gravando TXT",
        TranslationStage.RetryScheduled => "Nova tentativa agendada",
        TranslationStage.Saved => "Salvo no TXT", TranslationStage.Error => "Erro", _ => "Interrompido",
    };
    private static Vector4 StageColor(TranslationStage stage) => stage switch
    {
        TranslationStage.Saved => new(0.45f, 0.9f, 0.6f, 1), TranslationStage.Error => new(1, 0.55f, 0.5f, 1),
        TranslationStage.AwaitingResponse or TranslationStage.Validating or TranslationStage.Saving => new(0.5f, 0.8f, 1, 1),
        _ => new(1, 0.85f, 0.5f, 1),
    };
    private static string Preview(string text)
    {
        var length = Math.Min(65, text.Length);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].Replace('\n', ' ') + (length < text.Length ? "…" : "");
    }
    private static string AmountLabel(TextAmount amount) => $"{amount.Characters:N0} caracteres | {amount.Words:N0} palavras*";
    private static void ColoredWrapped(TranslationStage stage, string text)
    { ImGui.PushStyleColor(ImGuiCol.Text, StageColor(stage)); ImGui.TextWrapped(text); ImGui.PopStyleColor(); }

    private void DrawProgress()
    {
        if (progressSnapshot == null || Environment.TickCount64 >= nextProgressRefresh)
        { progressSnapshot = progress.Snapshot(); nextProgressRefresh = Environment.TickCount64 + 250; }
        var snapshot = progressSnapshot;
        var totals = file.Statistics;
        var catalog = originals.Catalog;
        ImGui.TextUnformatted("Traduções permanentes no TXT");
        ImGui.TextColored(StageColor(TranslationStage.Saved), $"{totals.Count:N0} textos salvos");
        TextLines($"Originais: {AmountLabel(totals.Source)}", $"Português: {AmountLabel(totals.Portuguese)}");
        if (catalog.Ready && preparation.Initialized)
        {
            var audit = preparation.Verification;
            var confirmed = audit.Ready ? audit.ConfirmedCount : Math.Clamp(preparation.Cursor, 0, catalog.Ordered.Count);
            ImGui.ProgressBar(catalog.Ordered.Count == 0 ? 1 : (float)confirmed / catalog.Ordered.Count,
                new Vector2(-1, 30), $"{confirmed:N0} / {catalog.Ordered.Count:N0} textos ({(catalog.Ordered.Count == 0 ? 100 : 100.0 * confirmed / catalog.Ordered.Count):F2}%)");
            ImGui.TextWrapped(audit.Ready
                ? $"Conferência do TXT: {audit.ConfirmedCharacters:N0} / {catalog.CharacterCount:N0} caracteres confirmados. Restam {audit.MissingCount:N0} textos no catálogo."
                : $"Conferindo todo o TXT: {audit.Checked:N0} / {catalog.Ordered.Count:N0} textos examinados. A barra usa a sequência salva enquanto a conferência termina.");
        }
        else ImGui.TextWrapped($"Preparando índice: {originals.CompletedSheets:N0} / {originals.TotalSheets:N0} tabelas. O total do TXT já pode ser consultado.");
        ImGui.TextWrapped(preparation.Message);
        if (preparation.CurrentGroup.Length > 0)
            ImGui.TextWrapped($"{ExpansionLabel(preparation.CurrentExpansion)} | {preparation.CurrentGroup}");
        if (originals.Error != null) ImGui.TextWrapped(originals.Error);
#if !BRISA_READ_ONLY
        if (ImGui.Button(config.BackgroundPreparation ? "Pausar preparação" : "Retomar preparação"))
        {
            config.BackgroundPreparation = !config.BackgroundPreparation;
            if (config.BackgroundPreparation) RetryErrors(resumePreparation: true);
            PluginInterface.SavePluginConfig(config);
        }
        ImGui.SameLine();
        if (ImGui.Button("Tentar novamente após erro##Progress")) RetryErrors(resumePreparation: true);
        if (ImGui.Checkbox("Preparação contínua##Progress", ref config.ContinuousPreparation))
        { preparation.Resume(); PluginInterface.SavePluginConfig(config); }
        if (ImGui.Button("Conferir se falta alguma tradução")) preparation.Verification.Restart();
#endif
        if (file.Warning != null) ImGui.TextWrapped(file.Warning);
        if ((queue.PausedReason ?? clientQueue.PausedReason) is { } reason) ImGui.TextWrapped(reason);
        if ((queue.RetryWarning ?? clientQueue.RetryWarning) is { } retryWarning) ImGui.TextWrapped(retryWarning);
        if (queue.DeferredCount + clientQueue.DeferredCount > 0)
            ImGui.TextWrapped($"Pendências com nova tentativa automática: {queue.DeferredCount + clientQueue.DeferredCount:N0}. A fila continua; não é necessário reiniciar o jogo.");
        SectionDivider();
        ImGui.TextUnformatted($"Esta sessão — desde {snapshot.SessionStarted:HH:mm:ss}");
        TextLines($"Novos salvos: {snapshot.SavedCount:N0} textos | {snapshot.SavedSource.Characters:N0} caracteres originais",
            $"Português produzido e salvo: {AmountLabel(snapshot.SavedPortuguese)}");
        TextLines($"Envios iniciados: {snapshot.RequestCount:N0} requisições | {snapshot.SentAmount.Characters:N0} caracteres",
            $"Agora: {snapshot.RequestsInFlight:N0} requisições aguardando resposta | {snapshot.RequestsLastMinute:N0} envios nos últimos 60 segundos");
#if !BRISA_READ_ONLY
        if (queue.ProviderName == "OpenAI")
        {
            var rate = openAIRateLimiter.Snapshot(queue.RequestsPerMinute, queue.TokensPerMinute);
            TextLines($"Simultâneos: até {concurrency.Limit} lotes + 1 vaga da HUD. Limites: {rate.Rpm:N0} RPM / {rate.Tpm:N0} TPM.",
                $"Reserva nos últimos 60 s: {rate.Requests:N0} requisições / {rate.ReservedTokens:N0} tokens estimados.");
            if (rate.Waiting) ImGui.TextColored(StageColor(TranslationStage.WaitingLimit), "Há lotes aguardando limite ou nova tentativa da API.");
        }
#endif
        SectionDivider();
        ImGui.TextUnformatted("Etapas atuais da fila");
        DrawStageTotals(snapshot);
        if (ImGui.CollapsingHeader("Como ler os números / detalhes dos envios"))
        {
            ImGui.TextWrapped($"Texto enviado na sessão: {snapshot.SentTextCount:N0} trechos | {AmountLabel(snapshot.SentAmount)}");
            ImGui.TextWrapped("Ao terminar a conferência, a barra considera todos os originais encontrados no TXT, inclusive falas prontas fora de ordem. Nenhuma conclusão é anunciada enquanto faltar texto no catálogo.");
            ImGui.TextWrapped("Uma falha isolada volta depois de mais 50 traduções salvas; falhas repetidas esperam 100, 200, 400 e até 500. Se restarem apenas pendências, há uma tentativa por tempo (30 segundos, 2 minutos e intervalos crescentes até 1 hora). Pedidos de espera da API continuam sendo respeitados. Pendências ficam em arquivos locais e só são removidas após conferir a gravação no TXT.");
            ImGui.TextWrapped("Envios incluem novas tentativas e trechos de textos longos; não são textos únicos. Um envio indica início da requisição, e não confirmação de entrega. A reserva de tokens é uma estimativa para controlar TPM, não o consumo faturado. Pausar impede adicionar mais textos; os lotes já na fila podem terminar e salvar.");
            ImGui.TextWrapped("* Palavras são unidades separadas por espaços contendo letras ou números. Para japonês, acompanhe caracteres; esta contagem não divide o japonês em palavras linguísticas.");
        }
        if (ImGui.BeginTabBar("BrisaProgressDetails"))
        {
            if (ImGui.BeginTabItem("Textos e etapas")) { DrawProgressTexts(snapshot); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Envios à API")) { DrawProgressRequests(snapshot); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        if (ImGui.Button("Abrir TXT##Progress")) OpenFile();
        ImGui.SameLine();
        if (ImGui.Button("Recarregar TXT##Progress")) { file.Reload(); RetryErrors(); }
    }

    private static void DrawStageTotals(ProgressSnapshot snapshot)
    {
        if (!ImGui.BeginTable("BrisaStageCounts", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg)) return;
        ImGui.TableSetupColumn("Etapa (original)", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Textos", ImGuiTableColumnFlags.WidthFixed, 85);
        ImGui.TableSetupColumn("Caracteres", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Palavras*", ImGuiTableColumnFlags.WidthFixed, 95); ImGui.TableHeadersRow();
        var rows = new (string Name, TranslationStage[] Stages)[]
        {
            ("Na fila — ainda sem lote", [TranslationStage.Queued]),
            ("Preparando / aguardando vaga", [TranslationStage.Preparing, TranslationStage.WaitingCapacity]),
            ("Esperando limite / nova tentativa", [TranslationStage.WaitingLimit, TranslationStage.Retrying]),
            ("Enviados — esperando resposta", [TranslationStage.AwaitingResponse]),
            ("Validando / gravando", [TranslationStage.Validating, TranslationStage.Saving]),
            ("Pendentes — nova tentativa depois", [TranslationStage.RetryScheduled]),
        };
        foreach (var row in rows)
        {
            var entries = snapshot.Texts.Where(x => row.Stages.Contains(x.Stage)).ToArray();
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextWrapped(row.Name);
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{entries.Length:N0}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{entries.Sum(x => x.Amount.Characters):N0}");
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{entries.Sum(x => x.Amount.Words):N0}");
        }
        ImGui.EndTable();
    }

    private void DrawProgressTexts(ProgressSnapshot snapshot)
    {
        ImGui.InputText("Buscar texto / grupo", ref progressSearch, 256);
        ImGui.Combo("Mostrar", ref progressFilter, "Em andamento\0Todos os recentes\0Salvos\0Erros / interrompidos\0Prioridade da HUD\0");
        bool Matches(ProgressText x) => (progressFilter switch
        {
            0 => !TranslationProgress.Terminal(x.Stage), 2 => x.Stage == TranslationStage.Saved,
            3 => x.Stage is TranslationStage.Error or TranslationStage.Interrupted or TranslationStage.RetryScheduled, 4 => x.Priority, _ => true,
        }) && (progressSearch.Length == 0 || (x.Source + "\n" + x.Translation + "\n" + x.Group).Contains(progressSearch, StringComparison.OrdinalIgnoreCase));
        var entries = snapshot.Texts.Where(Matches).OrderBy(x => TranslationProgress.Terminal(x.Stage)).ThenByDescending(x => x.Priority).ThenByDescending(x => x.Updated).Take(150).ToArray();
        ImGui.TextWrapped("Clique em um texto para ver o original, a tradução e as tentativas. Lista limitada aos 150 primeiros resultados; os totais da sessão continuam acumulando.");
        if (ImGui.BeginTable("BrisaProgressTextTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable, new Vector2(-1, 220)))
        {
            ImGui.TableSetupColumn("Etapa", ImGuiTableColumnFlags.WidthFixed, 180);
            ImGui.TableSetupColumn("Texto", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Caract. / envios", ImGuiTableColumnFlags.WidthFixed, 105);
            ImGui.TableSetupColumn("Origem", ImGuiTableColumnFlags.WidthFixed, 135); ImGui.TableHeadersRow();
            foreach (var item in entries)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ColoredWrapped(item.Stage, StageLabel(item.Stage));
                ImGui.TableNextColumn(); if (ImGui.Selectable(Preview(item.Source) + "##Text" + item.Id, selectedProgressText?.Id == item.Id)) selectedProgressText = item;
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{item.Amount.Characters:N0} / {item.Attempts}");
                ImGui.TableNextColumn(); ImGui.TextWrapped(item.Priority ? "HUD prioritária" : item.Group.Length > 0 ? ExpansionLabel(item.Expansion) : item.Language);
            }
            ImGui.EndTable();
        }
        if (selectedProgressText is { } selected)
        {
            selected = snapshot.Texts.FirstOrDefault(x => x.Id == selected.Id) ?? selected;
            selectedProgressText = selected;
            ImGui.TextColored(StageColor(selected.Stage), $"{StageLabel(selected.Stage)} | {selected.Provider} | {selected.Attempts} envio(s) | {selected.Updated:HH:mm:ss}");
            if (selected.Group.Length > 0) ImGui.TextWrapped($"{ExpansionLabel(selected.Expansion)} | {selected.Group}");
            if (selected.Error != null) ImGui.TextWrapped(selected.Error);
            if (selected.Stage == TranslationStage.RetryScheduled && selected.RetryAt is { } retryAt)
                ImGui.TextWrapped($"Tentará após mais {selected.RetryAfterTranslations:N0} textos salvos. Se restarem apenas pendências, poderá tentar a partir de {retryAt.ToLocalTime():HH:mm:ss}. Esperas mínimas da API prevalecem. Não é necessário reiniciar.");
            ImGui.TextWrapped("Original: " + selected.Source);
            if (selected.Translation != null) ImGui.TextWrapped("Português salvo: " + selected.Translation);
        }
    }

    private void DrawProgressRequests(ProgressSnapshot snapshot)
    {
        ImGui.InputText("Buscar texto enviado", ref progressSearch, 256);
        ImGui.TextWrapped("Requisições recentes desta sessão. Clique para ver os textos/trechos usados no envio. Os textos antigos no TXT não possuem histórico de API.");
        if (ImGui.BeginTable("BrisaProgressRequestTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(-1, 220)))
        {
            ImGui.TableSetupColumn("Horário / serviço", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Estado", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Trechos / caracteres", ImGuiTableColumnFlags.WidthFixed, 155);
            ImGui.TableSetupColumn("Duração", ImGuiTableColumnFlags.WidthFixed, 75); ImGui.TableHeadersRow();
            foreach (var item in snapshot.Requests.Take(100))
            {
                if (progressSearch.Length > 0 && !item.Texts.Any(x => x.Contains(progressSearch, StringComparison.OrdinalIgnoreCase))) continue;
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                if (ImGui.Selectable($"{item.Started:HH:mm:ss} {item.Provider}##Request{item.Id}", selectedProgressRequest?.Id == item.Id)) selectedProgressRequest = item;
                ImGui.TableNextColumn();
                var state = item.Stage is TranslationStage.Saved or TranslationStage.Saving ? "Resposta validada" : item.Stage == TranslationStage.Validating ? "Resposta recebida"
                    : item.Stage == TranslationStage.Retrying ? "Falha / repetição" : StageLabel(item.Stage);
                ColoredWrapped(item.Stage, state);
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{item.Texts.Length} / {item.Amount.Characters:N0}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{((item.Finished ?? DateTimeOffset.Now) - item.Started).TotalSeconds:F1} s");
            }
            ImGui.EndTable();
        }
        if (selectedProgressRequest is { } selected)
        {
            selected = snapshot.Requests.FirstOrDefault(x => x.Id == selected.Id) ?? selected;
            selectedProgressRequest = selected;
            ImGui.TextWrapped($"Envio #{selected.Id} | {selected.Provider} | {AmountLabel(selected.Amount)}" + (selected.Priority ? " | Prioridade da HUD" : ""));
            for (var i = 0; i < selected.Texts.Length; i++) ImGui.TextWrapped($"[{i + 1}] {selected.Texts[i]}");
        }
    }
}
