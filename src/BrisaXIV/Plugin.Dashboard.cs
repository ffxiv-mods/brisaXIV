using System.Numerics;
using BrisaXIV.Core;
using Dalamud.Bindings.ImGui;

namespace BrisaXIV;

public sealed partial class Plugin
{
    private enum DashboardPage { Progress, Costs, General, Translator, Hud, Preparation, File, Quests, Test, Categories }
    private DashboardPage dashboardPage = DashboardPage.Progress;
    private static readonly Vector4 Accent = new(0.65f, 0.43f, 0.96f, 1);
    private static readonly Vector4 Mint = new(0.37f, 0.83f, 0.68f, 1);
    private static readonly Vector4 Muted = new(0.62f, 0.64f, 0.72f, 1);
    private static readonly Vector4 Amber = new(1, 0.76f, 0.38f, 1);
    private static readonly Vector4 Panel = new(0.105f, 0.11f, 0.15f, 0.97f);
    private const float CardPadding = 18;
    private static readonly (ImGuiCol Key, Vector4 Color)[] ThemeColors =
    [
        (ImGuiCol.WindowBg, new(0.06f, 0.065f, 0.09f, 0.98f)), (ImGuiCol.ChildBg, Panel),
        (ImGuiCol.TitleBg, new(0.10f, 0.095f, 0.15f, 1)), (ImGuiCol.TitleBgActive, new(0.15f, 0.12f, 0.22f, 1)),
        (ImGuiCol.Text, new(0.90f, 0.90f, 0.94f, 1)), (ImGuiCol.Border, new(0.22f, 0.23f, 0.30f, 0.7f)),
        (ImGuiCol.FrameBg, new(0.16f, 0.17f, 0.22f, 1)), (ImGuiCol.FrameBgHovered, new(0.23f, 0.20f, 0.32f, 1)),
        (ImGuiCol.FrameBgActive, new(0.28f, 0.23f, 0.40f, 1)), (ImGuiCol.Button, new(0.24f, 0.19f, 0.35f, 1)),
        (ImGuiCol.ButtonHovered, new(0.36f, 0.26f, 0.52f, 1)), (ImGuiCol.ButtonActive, Accent),
        (ImGuiCol.Header, new(0.29f, 0.23f, 0.42f, 1)), (ImGuiCol.HeaderHovered, new(0.35f, 0.28f, 0.50f, 1)),
        (ImGuiCol.HeaderActive, new(0.40f, 0.29f, 0.61f, 1)), (ImGuiCol.CheckMark, Accent),
        (ImGuiCol.SliderGrab, Accent), (ImGuiCol.SliderGrabActive, new(0.78f, 0.61f, 1, 1)),
        (ImGuiCol.PlotHistogram, Accent), (ImGuiCol.Separator, new(0.24f, 0.23f, 0.31f, 0.7f)),
    ];
    private static void PushTheme()
    {
        foreach (var (key, color) in ThemeColors) ImGui.PushStyleColor(key, color);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 9f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(8, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 7));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 6));
    }
    private static void PopTheme() { ImGui.PopStyleVar(8); ImGui.PopStyleColor(ThemeColors.Length); }
    private static void SectionDivider()
    {
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
    }
    private static void TextLines(params string[] lines)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12, 5));
        foreach (var line in lines) ImGui.TextWrapped(line);
        ImGui.PopStyleVar(); ImGui.Spacing();
    }
    private static void BeginCard(string id)
    {
        ImGui.PushID(id); ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), 6));
        ImGui.Indent(CardPadding); ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, ImGui.GetContentRegionAvail().X - CardPadding));
        ImGui.PushItemWidth(Math.Max(120, (ImGui.GetContentRegionAvail().X - CardPadding) * 0.57f));
    }
    private static void EndCard()
    {
        ImGui.PopItemWidth(); ImGui.PopTextWrapPos(); ImGui.Unindent(CardPadding); ImGui.Dummy(new Vector2(0, 6)); ImGui.EndGroup();
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.26f, 0.33f, 0.7f)), 9);
        ImGui.PopID(); ImGui.Spacing();
    }
    private static bool ToggleRow(string label, ref bool value, string? help = null)
    {
        var start = ImGui.GetCursorScreenPos(); var localStart = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X - CardPadding;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(40, width - (help == null ? 64 : 108)));
        ImGui.TextUnformatted(label); ImGui.PopTextWrapPos();
        var height = Math.Max(32, ImGui.GetItemRectSize().Y);
        if (help != null)
        {
            ImGui.SetCursorScreenPos(new(start.X + width - 86, start.Y + Math.Max(0, (height - ImGui.GetTextLineHeight()) / 2)));
            ImGui.SmallButton("?##Help" + label);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip(); ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
                ImGui.TextUnformatted(label); ImGui.Separator(); ImGui.TextWrapped(help);
                ImGui.PopTextWrapPos(); ImGui.EndTooltip();
            }
        }
        ImGui.SetCursorScreenPos(new(start.X + width - 48, start.Y));
        var changed = ImGui.InvisibleButton("Switch##" + label, new Vector2(44, height));
        if (changed) value = !value;
        var draw = ImGui.GetWindowDrawList(); var top = start.Y + (height - 22) / 2;
        draw.AddRectFilled(new(start.X + width - 48, top), new(start.X + width - 4, top + 22),
            ImGui.GetColorU32(value ? Accent : new Vector4(0.23f, 0.24f, 0.29f, 1)), 11);
        draw.AddCircleFilled(new(start.X + width - (value ? 15 : 37), top + 11), 8, ImGui.GetColorU32(new Vector4(0.91f, 0.92f, 0.96f, 1)));
        ImGui.SetCursorPos(new(localStart.X, localStart.Y + height + ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Separator(); return changed;
    }
    private static (string Title, string Description) PageText(DashboardPage page) => page switch
    {
        DashboardPage.Progress => ("Progresso", "Traduções salvas, etapas da fila e envios à API."),
        DashboardPage.Costs => ("Custos da IA", "Tokens, estimativas e consulta opcional de custos da conta."),
        DashboardPage.General => ("Geral", "Originais japoneses, arquivo local e leitura acessível."),
        DashboardPage.Translator => ("Tradutor", "Provedor, credenciais protegidas e limites de envio."),
        DashboardPage.Hud => ("HUD e textos", "Cobertura das janelas e apresentação das falas."),
        DashboardPage.Categories => ("O que traduzir", "Escolha os textos que aparecem em português. Passe o mouse sobre ? para saber mais."),
        DashboardPage.Preparation => ("Preparação", "Continue o catálogo da expansão mais nova para a antiga."),
        DashboardPage.File => ("Arquivo de traduções", "Seu TXT permanente e recuperação de falhas."),
        DashboardPage.Quests => ("Missões", "Prepare as falas antes de iniciar uma missão."),
        _ => ("Teste de tradução", "Confira o resultado do provedor configurado."),
    };
    private void DrawDashboard()
    {
        if (selectProgressTab) { dashboardPage = DashboardPage.Progress; selectProgressTab = false; }
        if (dashboardPage == DashboardPage.Costs && !ShowAICosts) dashboardPage = DashboardPage.Translator;
        PushTheme();
        ImGui.SetNextWindowSize(new Vector2(1090, 760), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(850, 530), new Vector2(float.MaxValue, float.MaxValue));
        var visible = ImGui.Begin("Brisa XIV###BrisaSettings", ref settingsOpen);
        try
        {
            if (!visible) return;
            var draw = ImGui.GetWindowDrawList(); var origin = ImGui.GetWindowPos(); var size = ImGui.GetWindowSize();
            draw.AddCircleFilled(origin + new Vector2(size.X * 0.28f, 150), 180, ImGui.GetColorU32(new Vector4(0.35f, 0.20f, 0.55f, 0.07f)), 64);
            draw.AddCircleFilled(origin + new Vector2(size.X - 90, size.Y - 100), 240, ImGui.GetColorU32(new Vector4(0.17f, 0.35f, 0.40f, 0.05f)), 64);
            ImGui.TextColored(Accent, "BRISA XIV"); ImGui.SameLine();
            var paused = queue.PausedReason ?? clientQueue.PausedReason;
            ImGui.TextColored(paused != null ? Amber : config.Enabled ? Mint : Muted,
                paused != null ? "· Tradução pausada" : config.Enabled ? "· Ativo" : "· HUD desativada");
            ImGui.SameLine(); ImGui.TextColored(Muted, $"  {file.Count:N0} textos no TXT"); SectionDivider();
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 16));
            ImGui.BeginChild("Navigation", new Vector2(216, 0), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
            ImGui.PopStyleVar();
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12, 8));
            ImGui.TextColored(Muted, "ACOMPANHAMENTO");
            Nav(DashboardPage.Progress, "Progresso");
            if (ShowAICosts) Nav(DashboardPage.Costs, "Custos da IA");
            ImGui.Spacing(); ImGui.TextColored(Muted, "CONFIGURAÇÕES");
            Nav(DashboardPage.General, "Geral"); Nav(DashboardPage.Translator, "Tradutor"); Nav(DashboardPage.Hud, "HUD e textos");
            Nav(DashboardPage.Categories, "O que traduzir");
#if !BRISA_READ_ONLY
            Nav(DashboardPage.Preparation, "Preparação");
#endif
            Nav(DashboardPage.File, "Arquivo TXT");
#if !BRISA_READ_ONLY
            ImGui.Spacing(); ImGui.TextColored(Muted, "FERRAMENTAS"); Nav(DashboardPage.Quests, "Missões"); Nav(DashboardPage.Test, "Teste de tradução");
#endif
            SectionDivider(); ImGui.TextColored(Muted, "Versão 0.8.4 · API 15");
            if (ImGui.Button("Leitura ampliada", new Vector2(-1, 0))) readingOpen = true;
            ImGui.PopStyleVar(); ImGui.EndChild(); ImGui.SameLine(0, 18);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
            ImGui.BeginChild("DashboardContent", new Vector2(0, 0), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
            ImGui.PopStyleColor();
            var page = PageText(dashboardPage);
            ImGui.SetWindowFontScale(1.18f); ImGui.TextUnformatted(page.Title); ImGui.SetWindowFontScale(1);
            ImGui.TextColored(Muted, page.Description); SectionDivider();
            if (uiError != null) ImGui.TextWrapped(uiError);
            switch (dashboardPage)
            {
                case DashboardPage.Progress: DrawProgress(); break;
                case DashboardPage.Costs: DrawAICosts(); break;
                case DashboardPage.General: DrawGeneral(); break;
                case DashboardPage.Hud: DrawHudSettings(); break;
                case DashboardPage.Categories: DrawTextCategories(); break;
                case DashboardPage.File: DrawFileSettings(); break;
                case DashboardPage.Translator: DrawTranslatorSettings(); break;
#if !BRISA_READ_ONLY
                case DashboardPage.Preparation: DrawPreparationSettings(); break;
                case DashboardPage.Quests: BeginCard("Quests"); DrawPrefetch(); EndCard(); break;
                case DashboardPage.Test: DrawTranslatorTest(); break;
#endif
            }
            ImGui.EndChild();
        }
        finally { ImGui.End(); PopTheme(); }
    }
    private void Nav(DashboardPage page, string label)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0.5f));
        if (ImGui.Selectable(label + "##Nav", dashboardPage == page, ImGuiSelectableFlags.None, new Vector2(0, 34)))
            dashboardPage = page;
        ImGui.PopStyleVar();
    }
    private void DrawGeneral()
    {
        BeginCard("GeneralSwitches"); ImGui.TextUnformatted("Tradução e originais");
        var changed = ToggleRow("Ativar tradução na HUD", ref config.Enabled);
        var sourceChanged = ToggleRow("Consultar originais japoneses da instalação", ref config.JapaneseOriginals);
        sourceChanged |= ToggleRow("HUD lê somente o TXT", ref config.CachedOnly);
        sourceChanged |= ToggleRow("Usar idioma atual se não houver original japonês", ref config.TranslateUnmatched);
#if !BRISA_READ_ONLY
        sourceChanged |= ToggleRow("Priorizar falas e escolhas do NPC", ref config.PrioritizeDialogue);
#endif
        if (sourceChanged) { router = MakeRouter(); changed = true; }
        if (changed) PluginInterface.SavePluginConfig(config);
        ImGui.TextWrapped("O jogo pode continuar em inglês. Textos já salvos são lidos imediatamente do TXT. Falas abertas recebem prioridade; nomes de NPCs são preservados."); EndCard();
        BeginCard("AccessibleReading"); ImGui.TextUnformatted("Leitura ampliada");
        var readingChanged = ImGui.SliderFloat("Tamanho da letra", ref config.ReadingScale, 1f, 2.5f, "%.2fx");
        readingChanged |= ToggleRow("Mostrar também o original no painel", ref config.ShowJapanese);
        if (readingChanged) PluginInterface.SavePluginConfig(config);
        if (ImGui.Button("Abrir leitura / histórico")) readingOpen = true; EndCard();
        BeginCard("SourceStatus"); ImGui.TextUnformatted("Índice japonês");
        ImGui.TextWrapped($"{originals.Texts.Count:N0} correspondências · {originals.CompletedSheets:N0}/{originals.TotalSheets:N0} tabelas. " + (originals.Texts.Ready ? "Preparado." : "Preparando…"));
        if (originals.Error != null) ImGui.TextWrapped(originals.Error); EndCard();
    }
    private void DrawHudSettings()
    {
        BeginCard("HudCoverage"); ImGui.TextUnformatted("Apresentação no jogo");
        var changed = ToggleRow("Português nas próprias caixas da HUD", ref config.NativeHud);
        if (ImGui.Button("Escolher categorias de texto")) dashboardPage = DashboardPage.Categories;
        if (changed) PluginInterface.SavePluginConfig(config); EndCard();
        BeginCard("DialogueDisplay"); ImGui.TextUnformatted("Falas novas");
        changed = ToggleRow("Mostrar Traduzindo… enquanto aguarda", ref config.WaitForDialogue);
        if (config.WaitForDialogue) changed |= ImGui.SliderInt("Espera máxima (segundos)", ref config.DialogueWaitSeconds, 1, 30);
        if (changed) PluginInterface.SavePluginConfig(config);
        ImGui.TextWrapped("A caixa permanece visível e recebe o português automaticamente. Ao atingir a espera máxima ou ocorrer um erro, mostra o original. Traduções salvas não aguardam.");
        ImGui.TextWrapped("O texto é quebrado pela largura e fonte do jogo. Balões compatíveis crescem verticalmente. Confirmações Sim/Não e OK aumentam quando necessário, reservando espaço para os botões. Campos incompatíveis ficam disponíveis na leitura ampliada."); EndCard();
    }
    private void DrawFileSettings()
    {
        BeginCard("PermanentFile"); ImGui.TextUnformatted($"{file.Count:N0} traduções permanentes");
        ImGui.TextWrapped(file.Path);
        if (ImGui.Button("Abrir TXT")) OpenFile(); ImGui.SameLine();
        if (ImGui.Button("Recarregar TXT")) { file.Reload(); RetryErrors(); }
        ImGui.TextWrapped("Cada tradução é gravada imediatamente. A cópia .bak permite recuperar o arquivo. A conferência do catálogo procura textos ausentes, inclusive antes do cursor salvo.");
        if (file.Warning != null) ImGui.TextWrapped(file.Warning); EndCard();
        BeginCard("Recovery"); ImGui.TextUnformatted("Recuperação");
        ImGui.TextWrapped(queue.PausedReason ?? clientQueue.PausedReason ?? "Falhas isoladas voltam automaticamente depois de outros textos salvos, sem interromper a fila.");
        if (ImGui.Button("Tentar novamente após erro")) RetryErrors(resumePreparation: true);
        if (captureStopped && ImGui.Button("Retomar captura")) { captureStopped = false; uiError = null; }
        if (ImGui.Button("Conferir traduções que faltam")) preparation.Verification.Restart(); EndCard();
    }
    private void DrawTranslatorSettings()
    {
        BeginCard("TranslatorConfig");
#if !BRISA_READ_ONLY
        var provider = (int)config.Provider;
        if (ImGui.Combo("Tradutor", ref provider, "DeepL\0Ollama local\0Google Tradutor web\0OpenAI — GPT-6 Luna\0")) config.Provider = (TranslationProvider)provider;
        if (config.Provider == TranslationProvider.DeepL)
        {
            ImGui.InputText("Chave DeepL", ref apiKey, 512, ImGuiInputTextFlags.Password);
            ToggleRow("DeepL API Pro (pode cobrar por uso)", ref config.DeepLPro);
            ToggleRow("Google quando a cota DeepL acabar", ref config.GoogleFallback);
            ImGui.TextWrapped("Use a chave DeepL API. O endpoint padrão é API Free; a conta do tradutor web é separada.");
        }
        else if (config.Provider == TranslationProvider.Ollama)
        { ImGui.InputText("Endereço local", ref config.OllamaUrl, 512); ImGui.InputText("Modelo instalado", ref config.OllamaModel, 256); }
        else if (config.Provider == TranslationProvider.OpenAI)
        {
            ImGui.InputText("Chave OpenAI", ref openAIApiKey, 512, ImGuiInputTextFlags.Password);
            ImGui.InputText("Modelo OpenAI", ref config.OpenAIModel, 256);
            if (ImGui.InputInt("RPM", ref config.OpenAIRequestsPerMinute)) config.OpenAIRequestsPerMinute = Math.Clamp(config.OpenAIRequestsPerMinute, 1, 1_000_000);
            if (ImGui.InputInt("TPM", ref config.OpenAITokensPerMinute, 1000, 10000)) config.OpenAITokensPerMinute = Math.Clamp(config.OpenAITokensPerMinute, 1000, 1_000_000_000);
            ImGui.SliderInt("Lotes simultâneos", ref config.ParallelBatches, 1, 32);
            ImGui.TextWrapped("Padrão: gpt-6-luna, 5.000 RPM, 2.000.000 TPM e 4 lotes + 1 vaga da HUD. As filas compartilham limites e respeitam valores menores informados pelo servidor.");
            ImGui.TextWrapped("A API pode consumir saldo pago. Após salvar uma IA configurada, a página Custos da IA aparece na lateral.");
            if (openAIRateLimiter.IsWaiting) ImGui.TextColored(Amber, "Aguardando limite ou nova tentativa.");
        }
        else ImGui.TextWrapped("Google Tradutor web, sem chave adicional. A disponibilidade pode variar.");
        ImGui.TextWrapped("As chaves são protegidas pelo Windows. Novas traduções permanecem no mesmo TXT; o chat de jogadores não é capturado.");
        if (ImGui.Button("Salvar e aplicar tradutor")) Apply();
        if (queue.UsingGoogleFallback || clientQueue.UsingGoogleFallback) ImGui.TextColored(Amber, "Cota DeepL esgotada: usando Google Tradutor.");
#else
        ImGui.TextWrapped("DLL final somente de leitura. Os tradutores online foram removidos.");
#endif
        EndCard();
    }
#if !BRISA_READ_ONLY
    private void DrawPreparationSettings()
    {
        BeginCard("BackgroundPreparation");
        if (ToggleRow("Retomar preparação ao abrir o jogo", ref config.BackgroundPreparation)) PluginInterface.SavePluginConfig(config);
        if (ToggleRow("Preparação contínua sem limite por abertura", ref config.ContinuousPreparation))
        { preparation.Resume(); PluginInterface.SavePluginConfig(config); }
        if (!config.ContinuousPreparation && ImGui.InputInt("Caracteres por abertura", ref config.SessionCharacterBudget))
        { config.SessionCharacterBudget = Math.Clamp(config.SessionCharacterBudget, 1000, 1000000); PluginInterface.SavePluginConfig(config); }
        ImGui.TextWrapped("Enquanto o jogo estiver aberto, prepara textos da expansão mais nova para a antiga e cede a vez às falas. Desative Retomar preparação para parar de adicionar novos textos."); EndCard();
        BeginCard("PreparationStatus");
        ImGui.TextWrapped($"Catálogo: {originals.Catalog.Ordered.Count:N0} textos · {originals.Catalog.CharacterCount:N0} caracteres.");
        ImGui.TextWrapped(preparation.Message);
        if (preparation.CurrentGroup.Length > 0) ImGui.TextWrapped($"{ExpansionLabel(preparation.CurrentExpansion)} · {preparation.CurrentGroup}");
        if (!preparation.Completed && ImGui.Button("Retomar mais uma parte nesta sessão")) RetryErrors(resumePreparation: true);
        if (preparation.Completed && PluginInterface.IsDev && ImGui.Button("Gerar versão final somente de leitura")) GenerateReadOnly();
        ImGui.TextWrapped("Textos compartilhados sem expansão ficam após as missões. A conclusão exige conferir todos os originais no TXT."); EndCard();
    }
    private void DrawTranslatorTest()
    {
        BeginCard("TranslatorTest"); ImGui.TextWrapped("Um texto novo pode consumir cota ou saldo da API. Um texto salvo é lido do TXT.");
        ImGui.InputTextMultiline("##Teste", ref manualText, 6001, new Vector2(-1, 110));
        if (ImGui.Button("Traduzir texto de teste")) { manualRequested = TextRules.Normalize(manualText); queue.Get(manualRequested); }
        if (manualRequested.Length > 0) DrawTranslation(manualRequested, true); EndCard();
    }
#endif
}
