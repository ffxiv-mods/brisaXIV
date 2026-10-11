using System.Diagnostics;
using System.Numerics;
using BrisaXIV.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game;

namespace BrisaXIV;

public sealed partial class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle Lifecycle { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static ISeStringEvaluator Evaluator { get; private set; } = null!;
    private readonly Configuration config;
    private readonly TranslationFile file;
    private readonly TranslationProgress progress = new();
    private readonly TranslationConcurrency concurrency = new();
    private readonly GameTextCapture capture = new();
    private TranslationQueue queue;
    private TranslationQueue clientQueue;
    private TranslationRouter router;
#if !BRISA_READ_ONLY
    // What the main path cannot deliver goes to Google, OpenAI and DeepL, in that order,
    // among the providers with a key; see TranslationRouter. Extra queues are owned here.
    private IReadOnlyList<FallbackQueues> fallback = [];
    private readonly List<TranslationQueue> fallbackQueues = [];
#endif
    private readonly TranslationJournal journal;
    private readonly HoveredTextSources hovered;
    private readonly PrefetchedSources prefetched = new();
    private readonly ISourceTextResolver liveSources;
#if !BRISA_READ_ONLY
    private readonly TranslationPrefetch prefetch;
#endif
    private readonly NamePlateTranslation namePlates;
    private readonly TalkRefreshTranslation talkRefresh;
    private readonly JapaneseSourceIndex originals;
    private readonly BackgroundPreparation preparation;
    private IReadOnlyList<CapturedWindow> windows = [];
    private readonly List<CapturedWindow> history = [];
    private volatile CapturedWindow[] historySnapshot = [];
    private readonly Dictionary<string, string> lastDialogues = new();
    private string apiKey = "";
#if !BRISA_READ_ONLY
    private string openAIApiKey = "";
    private readonly OpenAIRateLimiter openAIRateLimiter = new();
    private readonly GoogleCadence googleCadence = new();
    private readonly object diagnosticGate = new();
#endif
    private string? uiError;
#if !BRISA_READ_ONLY
    private string manualText = "こんにちは。";
    private string manualRequested = "";
#endif
    private bool settingsOpen = true;
    private bool readingOpen;
    private bool captureStopped;
    private bool completedModeApplied;
    private long nextScan;
    private readonly QuestCatalog quests;
    private string questSearch = "";
    private IReadOnlyList<QuestEntry> questResults = [];
    private QuestEntry? selectedQuest;
    private string[] questLines = [];
    private int prefetchIndex;
    private bool prefetchRunning;
    private string? prefetchMessage;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Version < 3)
        {
            config.OtherWindows = config.JapaneseOriginals = config.Notices = config.GoogleFallback = config.TranslateUnmatched = true;
            config.BackgroundPreparation = config.CachedOnly = true;
            config.SessionCharacterBudget = 30000;
            config.Version = 3;
            PluginInterface.SavePluginConfig(config);
        }
        if (config.Version < 4)
        { config.PrioritizeDialogue = true; config.Version = 4; PluginInterface.SavePluginConfig(config); }
        if (config.Version < 5)
        {
            config.Subtitles = config.Dialogue;
            config.Actions = config.Items;
            config.Confirmations = config.Menus;
            config.WindowTitles = true;
            config.Version = 5;
            PluginInterface.SavePluginConfig(config);
        }
        if (config.Version < 6)
        { config.NamePlates = config.CharacterNames; config.Version = 6; PluginInterface.SavePluginConfig(config); }
        if (config.Version < 7)
        { RepairSilencedCategories(); config.Version = 7; PluginInterface.SavePluginConfig(config); }
#if BRISA_READ_ONLY
        config.BackgroundPreparation = false;
        config.CachedOnly = true;
#endif
        // Developer installs keep the current project path. Installed packages use
        // persistent Dalamud data, because the DLL directory is replaced on updates.
        var packageRoot = PluginInterface.AssemblyLocation.Directory!.FullName;
        var root = PluginInterface.IsDev ? packageRoot : PluginInterface.GetPluginConfigDirectory();
        if (!PluginInterface.IsDev) ImportPackageTranslations(packageRoot, root);
        file = new TranslationFile(Path.Combine(root, "traducoes.txt"));
        if (!PluginInterface.IsDev) MergePackageTranslations(packageRoot, root);
        InitializeUsage(root);
#if !BRISA_READ_ONLY
        if (config.EncryptedApiKey.Length == 0 && File.Exists(Path.Combine(packageRoot, "chave.dpapi")))
            config.EncryptedApiKey = File.ReadAllText(Path.Combine(packageRoot, "chave.dpapi")).Trim();
        try { apiKey = KeyProtection.Unprotect(config.EncryptedApiKey); }
        catch { uiError = "A chave salva não pôde ser lida neste usuário do Windows. Informe-a novamente."; }
        try { openAIApiKey = KeyProtection.Unprotect(config.EncryptedOpenAIApiKey); }
        catch { uiError = "A chave OpenAI salva não pôde ser lida neste usuário do Windows. Informe-a novamente."; }
#endif
        journal = new TranslationJournal(Path.Combine(root, "traducoes-novas.txt"));
        queue = MakeQueue();
        clientQueue = MakeQueue(false);
#if !BRISA_READ_ONLY
        // 0.0.5 sent every unmatched tooltip line (counters, prices) to Google from the client
        // language; that retry list is discarded in favor of the narrower one used now.
        try { File.Delete(Path.Combine(root, "pendencias-google-" + ClientLanguageCode + ".json")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        fallback = MakeFallback();
#endif
        originals = new JapaneseSourceIndex(DataManager.GameData.DataPath.FullName, (Lumina.Data.Language)((int)ClientState.ClientLanguage + 1), root);
        preparation = new BackgroundPreparation(originals.Catalog, file, Path.Combine(root, "progresso-preparacao.json"));
        var gameTexts = new GameTextEvaluator(DataManager, Evaluator, ClientState.ClientLanguage);
        hovered = new HoveredTextSources(GameGui, gameTexts);
        liveSources = new CompositeSourceResolver(hovered, prefetched);
        router = MakeRouter();
#if !BRISA_READ_ONLY
        prefetch = new TranslationPrefetch(DataManager, gameTexts, prefetched, file, () => prefetchTarget, config);
#endif
        namePlates = new NamePlateTranslation(NamePlateGui, () => router, () => config.Enabled && config.NamePlates);
        capture.Injected = new InjectedTextMap();
        talkRefresh = new TalkRefreshTranslation(() => router, config, capture.Injected);
        quests = new QuestCatalog(DataManager);
        Commands.AddHandler("/brisa", new CommandInfo(OnCommand) { HelpMessage = "Progresso: /brisa progresso. Custos da IA: /brisa custos. Configuração: /brisa. Leitura: /brisa ler. Todas as telas: /brisa tudo. Recarregar TXT: /brisa recarregar. Diagnóstico do diário, habilidades e itens: /brisa diagnostico." });
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi += OpenReading;
        Framework.Update += Update;
        Lifecycle.RegisterListener(AddonEvent.PreDraw, BeforeDraw);
        Lifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, ["ActionDetail", "ItemDetail"], BeforeTooltipUpdate);
        Lifecycle.RegisterListener(AddonEvent.PreRefresh, "Talk", BeforeTalkRefresh);
    }

    private void BeforeTalkRefresh(AddonEvent type, AddonArgs args)
    {
        if (captureStopped || !ClientState.IsLoggedIn) return;
        try { talkRefresh.PreRefresh(type, args); }
        catch (Exception ex) { Log.Warning("BrisaXIV: tradução da fala no refresh ignorada ({ErrorType}).", ex.GetType().Name); }
    }

    private void BeforeTooltipUpdate(AddonEvent type, AddonArgs args)
    {
        if (captureStopped) return;
        try { capture.BeforeTooltipUpdate(args.Addon.Address); }
        catch { captureStopped = true; uiError = "Falha ao preparar um tooltip: captura pausada. Reabra a janela do jogo e use Retomar captura."; }
    }

    private string ClientLanguageCode => ClientState.ClientLanguage switch { ClientLanguage.Japanese => "JA", ClientLanguage.German => "DE", ClientLanguage.French => "FR", _ => "EN" };
    private TranslationQueue MakeQueue(bool japanese = true)
    {
#if BRISA_READ_ONLY
        return new(file, new Translator(new(config.Provider, "", config.DeepLPro, config.OllamaUrl, config.OllamaModel)), progress: progress);
#else
        var parallel = config.Provider == TranslationProvider.OpenAI ? Math.Clamp(config.ParallelBatches, 1, 32) : 1;
        concurrency.SetLimit(parallel);
        return new(file, new Translator(new(config.Provider, apiKey, config.DeepLPro, config.OllamaUrl, config.OllamaModel,
            japanese ? "JA" : ClientLanguageCode, config.GoogleFallback, openAIApiKey, config.OpenAIModel,
            config.OpenAIRequestsPerMinute, config.OpenAITokensPerMinute), openAIRateLimiter: openAIRateLimiter, openAIDiagnostic: OpenAIDiagnostic, aiUsage: usageLedger.Record, googleCadence: googleCadence),
            capacity: Math.Max(128, parallel * 40), progress: progress, parallelism: parallel,
            concurrency: config.Provider == TranslationProvider.OpenAI ? concurrency : null,
            retryPath: Path.Combine(Path.GetDirectoryName(file.Path)!, japanese ? "pendencias-ja.json" : "pendencias-cliente-" + ClientLanguageCode + ".json"),
            // The Japanese queue also runs the bulk preparation; it is journaled only once the catalog is
            // complete, when everything it translates is text found missing while playing.
            stored: japanese ? JournalAfterPreparation : journal.Append);
#endif
    }
#if !BRISA_READ_ONLY
    private void JournalAfterPreparation(string source, string translation)
    {
        if (preparation is { Completed: true }) journal.Append(source, translation);
    }

    // Google, OpenAI and DeepL, in that order, among the providers with a key (Google needs none).
    // The main provider reuses its queues; the others get their own, journaled as new translations.
    private IReadOnlyList<FallbackQueues> MakeFallback()
    {
        foreach (var extra in fallbackQueues) extra.Dispose();
        fallbackQueues.Clear();
        // Google answers fastest (needs no key), so on-screen text appears sooner; a failure moves on.
        var google = ProviderQueues(TranslationProvider.Google);
        var openAI = string.IsNullOrWhiteSpace(openAIApiKey) ? null : ProviderQueues(TranslationProvider.OpenAI);
        var deepL = string.IsNullOrWhiteSpace(apiKey) ? null : ProviderQueues(TranslationProvider.DeepL);
        // Background prefetch prefers a provider with a key; the public Google endpoint is paced slowly.
        prefetchTarget = openAI != null ? new(openAI.Japanese, false) : deepL != null ? new(deepL.Japanese, false) : new(google.Japanese, true);
        return new[] { google, openAI, deepL }.OfType<FallbackQueues>().ToArray();
    }
    private PrefetchTarget? prefetchTarget;

    private FallbackQueues ProviderQueues(TranslationProvider provider)
    {
        if (provider == config.Provider) return new(queue, clientQueue);
        var queues = new FallbackQueues(MakeProviderQueue(provider, true), MakeProviderQueue(provider, false));
        fallbackQueues.Add(queues.Japanese); fallbackQueues.Add(queues.Client);
        return queues;
    }

    private TranslationQueue MakeProviderQueue(TranslationProvider provider, bool japanese)
    {
        var name = provider switch { TranslationProvider.OpenAI => "openai", TranslationProvider.DeepL => "deepl", _ => "google" };
        return new(file, new Translator(new(provider, apiKey, config.DeepLPro, config.OllamaUrl, config.OllamaModel,
                japanese ? "JA" : ClientLanguageCode, false, openAIApiKey, config.OpenAIModel,
                config.OpenAIRequestsPerMinute, config.OpenAITokensPerMinute),
                openAIRateLimiter: openAIRateLimiter, openAIDiagnostic: OpenAIDiagnostic, aiUsage: usageLedger.Record, googleCadence: googleCadence),
            progress: progress,
            retryPath: Path.Combine(Path.GetDirectoryName(file.Path)!, $"pendencias-{name}-" + (japanese ? "ja.json" : "cliente-" + ClientLanguageCode + ".json")),
            stored: journal.Append);
    }
#endif
    private bool DialoguePriorityEnabled =>
#if BRISA_READ_ONLY
        false;
#else
        config.PrioritizeDialogue && !preparation.Completed;
#endif
    private TranslationRouter MakeRouter() => new(queue, file, originals.Texts, config.JapaneseOriginals, config.TranslateUnmatched || !config.JapaneseOriginals ? clientQueue : null, config.CachedOnly || preparation.Completed, DialoguePriorityEnabled,
#if BRISA_READ_ONLY
        null,
#else
        config.GoogleForMissing ? fallback : null,
#endif
        liveSources);
    private void RetryErrors(bool resumePreparation = false)
    {
        shownBillingFailure = null; queue.RetryErrors(); clientQueue.RetryErrors();
#if !BRISA_READ_ONLY
        foreach (var extra in fallbackQueues) extra.RetryErrors();
#endif
        if (resumePreparation) preparation.Resume();
    }
    private void RestartQueues()
    {
        shownBillingFailure = null; queue.Dispose(); clientQueue.Dispose(); concurrency.ClearBillingPause(); queue = MakeQueue(); clientQueue = MakeQueue(false);
#if !BRISA_READ_ONLY
        fallback = MakeFallback();
#endif
        router = MakeRouter();
    }
    private void OpenSettings() => settingsOpen = true;
    private void OpenReading() => readingOpen = true;
    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "ler": readingOpen = !readingOpen; break;
            case "progresso": settingsOpen = true; selectProgressTab = true; break;
            case "custos": settingsOpen = true; dashboardPage = DashboardPage.Costs; break;
            case "categorias": settingsOpen = true; dashboardPage = DashboardPage.Categories; break;
            case "jornal": config.Journal = !(config.Journal ?? config.Quests); PluginInterface.SavePluginConfig(config); break;
            case "recarregar": file.Reload(); RetryErrors(); break;
            case "tudo": config.Enabled = config.NativeHud = true; SetAllTextCategories(true); break;
            case "diagnostico" or "diagnóstico":
                capture.Diagnostics ??= new HudDiagnostics(Path.Combine(Path.GetDirectoryName(file.Path)!, "diagnostico-hud.log"));
                capture.Diagnostics.Toggle();
                var diagnostics = capture.Diagnostics;
                hovered.Changed = diagnostics.Enabled ? summary => diagnostics.Record(0, "tooltip-avaliado", "ActionDetail", summary) : null;
                settingsOpen = true;
                uiError = capture.Diagnostics.Enabled
                    ? "Diagnóstico (diário, habilidades e itens) LIGADO. Reproduza o problema e use /brisa diagnostico de novo para desligar. Arquivo: " + capture.Diagnostics.Path
                    : "Diagnóstico desligado. Arquivo: " + capture.Diagnostics.Path;
                break;
            default: settingsOpen = !settingsOpen; break;
        }
    }

    private void Update(IFramework framework)
    {
        ShareBillingPause();
        preparation.Pump(queue, config.BackgroundPreparation, config.SessionCharacterBudget, queue.PriorityPendingCount + clientQueue.PriorityPendingCount > 0, config.ContinuousPreparation);
        if (preparation.Completed != completedModeApplied)
        {
            completedModeApplied = preparation.Completed;
            if (completedModeApplied) { config.CachedOnly = true; PluginInterface.SavePluginConfig(config); }
            router = MakeRouter();
        }
        if (!ClientState.IsLoggedIn) { windows = []; lastDialogues.Clear(); return; }
        if (captureStopped || Environment.TickCount64 < nextScan) return;
        nextScan = Environment.TickCount64 + 150;
        PumpPrefetch();
        try
        {
            hovered.Refresh();
#if !BRISA_READ_ONLY
            prefetch.Tick();
#endif
            namePlates.Pump();
            // Player-named companions (chocobo, race chocobo) are never treated as NPC names.
            capture.SetNpcNames(Objects.Where(x => x.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc
                    or Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc
                && x is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc { BattleNpcKind: Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Buddy
                    or Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Player or Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.RaceChocobo })
                .Select(x => x.Name.ToString()));
            windows = capture.Scan(config, router);
            foreach (var dialogue in windows.Where(x => x.Category == "Falas"))
            {
                var signature = string.Join("\n", dialogue.Lines);
                if (lastDialogues.TryGetValue(dialogue.Name, out var previous) && previous == signature) continue;
                lastDialogues[dialogue.Name] = signature;
                history.Insert(0, dialogue);
                if (history.Count > 80) history.RemoveAt(history.Count - 1);
                historySnapshot = history.ToArray();
            }
        }
        catch (Exception ex)
        {
            captureStopped = true;
            uiError = "A captura foi pausada após uma falha. Abra Configurações e use Retomar captura. Mudanças no jogo podem exigir atualização do plugin.";
            Log.Error("BrisaXIV paused capture ({ErrorType}).", ex.GetType().Name);
        }
    }

    private void BeforeDraw(AddonEvent type, AddonArgs args)
    {
        if (captureStopped || !ClientState.IsLoggedIn) return;
        try { capture.BeforeDraw(args.Addon.Address, config, router); }
        catch
        {
            try { capture.ReleaseHold(args.Addon.Address); } catch { }
            captureStopped = true; uiError = "Falha na HUD: captura pausada. Reabra a janela do jogo e use Retomar captura.";
        }
    }

    private void PumpPrefetch()
    {
        if (!prefetchRunning) return;
        if (queue.PriorityPendingCount + clientQueue.PriorityPendingCount > 0) return;
        while (prefetchIndex < questLines.Length && queue.PendingCount < 64)
        {
            var result = queue.Get(questLines[prefetchIndex]);
            if (result.Error != null && !result.Pending) { prefetchRunning = false; prefetchMessage = result.Error; return; }
            prefetchIndex++;
        }
        if (prefetchIndex == questLines.Length && queue.PendingCount == 0)
        {
            var missing = questLines.FirstOrDefault(x => !file.TryGet(x, out _));
            if (missing == null) { prefetchRunning = false; prefetchMessage = "Pré-tradução concluída. Todas as linhas foram conferidas no TXT."; }
            else
            {
                var status = queue.Get(missing);
                prefetchMessage = status.Pending ? "Restam linhas pendentes, com nova tentativa automática enquanto os outros textos continuam." : status.Error;
                if (status.Error != null && !status.Pending) prefetchRunning = false;
            }
        }
    }

    private void Draw()
    {
        if (settingsOpen) DrawSettings();
        if (readingOpen) DrawReading();
        DrawBillingAlert();
    }

    private void DrawSettings()
    {
        DrawDashboard();
    }


    private static string ExpansionLabel(int expansion) => expansion switch
    {
        5 => "Dawntrail", 4 => "Endwalker", 3 => "Shadowbringers", 2 => "Stormblood", 1 => "Heavensward",
        0 => "A Realm Reborn", -1 => "Textos compartilhados", _ => $"Expansão {expansion}",
    };

    private void GenerateReadOnly()
    {
        try
        {
            var script = Path.Combine(PluginInterface.AssemblyLocation.Directory!.Parent!.FullName, "Gerar-Somente-Leitura.ps1");
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
            });
            uiError = "Conversão iniciada. Confira plugin/conversao-final.log e recarregue o Brisa após a confirmação de sucesso.";
        }
        catch { uiError = "Não foi possível iniciar a conversão. Execute Gerar-Somente-Leitura.ps1 na pasta do projeto."; }
    }

    private void DrawPrefetch()
    {
        ImGui.TextUnformatted("Pré-traduzir uma missão antes de iniciá-la");
        ImGui.TextWrapped("Busque pelo nome em inglês ou japonês, ou pelo ID. A busca e a preparação não consultam o DeepL. Textos dinâmicos podem precisar de tradução quando aparecerem.");
        ImGui.InputText("Missão", ref questSearch, 256);
        if (ImGui.Button("Buscar missões na instalação local"))
        {
            try { questResults = quests.Search(questSearch); prefetchMessage = questResults.Count == 0 ? "Sem resultados. Digite pelo menos 2 caracteres." : null; }
            catch { prefetchMessage = "Não foi possível ler o catálogo de missões. Confira a instalação e a versão do jogo."; }
        }
        foreach (var quest in questResults)
        {
            if (ImGui.Selectable($"{quest.EnglishName} — {quest.JapaneseName}##{quest.RowId}", selectedQuest?.RowId == quest.RowId))
            {
                if (prefetchRunning) { prefetchMessage = "Cancele a pré-tradução atual antes de escolher outra missão."; continue; }
                try { selectedQuest = quest; questLines = quests.GetLines(quest); prefetchIndex = 0; prefetchMessage = null; }
                catch (Exception ex) { questLines = []; prefetchMessage = ex is InvalidOperationException ? ex.Message : "Não foi possível preparar as falas dessa missão."; }
            }
        }
        if (selectedQuest != null && questLines.Length > 0)
        {
            var missing = questLines.Where(x => !file.TryGet(x, out _)).ToArray();
            ImGui.TextWrapped($"{selectedQuest.EnglishName}: {questLines.Length - missing.Length}/{questLines.Length} linhas salvas. Faltam {missing.Sum(x => x.Length)} caracteres (estimativa). Não é necessário abrir a missão.");
            if (!prefetchRunning && missing.Length > 0 && ImGui.Button($"Pré-traduzir {missing.Length} linhas novas"))
            { queue.RetryErrors(); prefetchIndex = 0; prefetchRunning = true; prefetchMessage = "Preparando…"; }
            if (prefetchRunning)
            {
                ImGui.ProgressBar((float)(questLines.Length - missing.Length) / questLines.Length, new Vector2(-1, 0));
                if (ImGui.Button("Cancelar pré-tradução"))
                { prefetchRunning = false; RestartQueues(); prefetchMessage = "Cancelado. Requisições já recebidas pelo provedor podem consumir cota; traduções concluídas foram mantidas."; }
            }
        }
        if (prefetchMessage != null) ImGui.TextWrapped(prefetchMessage);
    }

    private void Apply()
    {
        try
        {
            config.EncryptedApiKey = KeyProtection.Protect(apiKey);
#if !BRISA_READ_ONLY
            config.EncryptedOpenAIApiKey = KeyProtection.Protect(openAIApiKey);
#endif
            PluginInterface.SavePluginConfig(config);
            RestartQueues();
            preparation.Resume();
            uiError = null;
        }
        catch { uiError = "Não foi possível proteger ou salvar a chave. Confira as permissões do Windows."; }
    }

#if !BRISA_READ_ONLY
    private void OpenAIDiagnostic(string detail)
    {
        // Detail contains only fixed reason codes, counts and a validated request ID.
        Log.Warning("BrisaXIV OpenAI validation: {Detail}", detail);
        try
        {
            lock (diagnosticGate)
            {
                var path = Path.Combine(Path.GetDirectoryName(file.Path)!, "diagnostico-openai.log");
                if (File.Exists(path) && new FileInfo(path).Length > 65536) File.WriteAllText(path, "");
                File.AppendAllText(path, DateTimeOffset.UtcNow.ToString("O") + " " + detail + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
#endif

    private void OpenFile()
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true, ArgumentList = { file.Path } }); }
        catch { uiError = "Não foi possível abrir o editor. Abra manualmente o caminho indicado acima."; }
    }

    private void DrawReading()
    {
        ImGui.SetNextWindowSize(new Vector2(700, 470), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Brisa XIV — Leitura ampliada###BrisaReading", ref readingOpen)) { ImGui.End(); return; }
        try
        {
            ImGui.SetWindowFontScale(Math.Clamp(config.ReadingScale, 1f, 2.5f));
            if (ImGui.Button("Configurações")) settingsOpen = true;
            if (ImGui.BeginTabBar("BrisaTabs"))
            {
                if (ImGui.BeginTabItem("Na tela"))
                {
                    if (windows.Count == 0) ImGui.TextWrapped("Abra uma janela do jogo ou converse com um personagem. Você pode manter o idioma atual do cliente.");
                    foreach (var window in windows) DrawWindow(window);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Histórico de falas"))
                {
                    ImGui.TextWrapped("Últimas 80 falas desta sessão. As traduções ficam no arquivo de texto.");
                    foreach (var dialogue in historySnapshot) DrawWindow(dialogue, request: false);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        finally { ImGui.SetWindowFontScale(1f); ImGui.End(); }
    }

    private void DrawWindow(CapturedWindow window, bool request = true)
    {
        ImGui.Separator(); ImGui.TextUnformatted($"{window.Category} — {window.Label}");
        foreach (var line in window.Lines)
        {
            DrawTranslation(line, config.Enabled && request, window.Category);
            if (config.ShowJapanese) ImGui.TextWrapped(line);
            ImGui.Spacing();
        }
    }

    private void DrawTranslation(string source, bool request, string scope = "")
    {
        var result = router.Get(source, request, scope);
        ImGui.TextWrapped(result.Text ?? result.Error ?? (result.Pending ? "Traduzindo…" : "Texto ainda não preparado no TXT."));
    }

    public void Dispose()
    {
        Framework.Update -= Update;
        Lifecycle.UnregisterListener(AddonEvent.PreDraw, BeforeDraw);
        Lifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, ["ActionDetail", "ItemDetail"], BeforeTooltipUpdate);
        Lifecycle.UnregisterListener(AddonEvent.PreRefresh, "Talk", BeforeTalkRefresh);
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi -= OpenReading;
        Commands.RemoveHandler("/brisa");
        // Only freshly rediscovered live nodes are restored. Closed addons are never dereferenced.
        void Restore()
        {
            try { if (ClientState.IsLoggedIn) capture.Scan(config, router, restoreOnly: true); }
            catch { Log.Warning("BrisaXIV could not restore all native text. Reopen the affected game window."); }
        }
        if (Framework.IsInFrameworkUpdateThread) Restore();
        else _ = Framework.RunOnFrameworkThread(Restore);
        namePlates.Dispose();
        queue.Dispose();
        clientQueue.Dispose();
#if !BRISA_READ_ONLY
        foreach (var extra in fallbackQueues) extra.Dispose();
#endif
        originals.Dispose();
        history.Clear(); apiKey = "";
#if !BRISA_READ_ONLY
        billingStop.Cancel(); billingClient.Dispose();
        billingAdminKey = savedBillingAdminKey = "";
        if (billingTask == null) billingStop.Dispose();
        else _ = billingTask.ContinueWith(_ => billingStop.Dispose(), TaskScheduler.Default);
        openAIApiKey = "";
#endif
    }
}
