using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using BrisaXIV.Core;

internal static class FallbackRoutingTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request).WaitAsync(token);
    }
    private sealed class Live(Dictionary<string, string> pairs) : ISourceTextResolver
    {
        public bool TryResolve(string display, string scope, out string japanese) => pairs.TryGetValue(display, out japanese!);
    }
    private static async Task Until(Func<bool> check)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!check()) { if (DateTime.UtcNow >= end) throw new Exception("Fallback test timeout"); await Task.Delay(15); }
    }

    // Fake Google web endpoint: records (source language, text) and answers "PT:" + text.
    private static Handler Google(ConcurrentQueue<(string Language, string Text)> sent) => new(async request =>
    {
        var form = await request.Content!.ReadAsStringAsync();
        var fields = form.Split('&').Select(x => x.Split('=')).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1].Replace('+', ' ')));
        sent.Enqueue((fields["sl"], fields["q"]));
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new object[] { new object[] { new object[] { "PT:" + fields["q"], fields["q"] } } })) };
    });

    public static async Task Run(Action<bool, string> check, string scratch)
    {
        var names = new SourceTextIndex { Ready = true };
        names.Add("striking dummy", "木人", "Outras janelas");
        check(names.TryResolve("Striking Dummy", "Outras janelas", out var mapped) && mapped == "木人"
            && names.TryResolve("STRIKING DUMMY", "", out mapped) && mapped == "木人",
            "Enemy names stored in lowercase resolve when the HUD shows them capitalized");
        names.Add("Alphinaud", "アルフィノ", "Falas"); names.Add("Alphinaud", "アルフィノ様", "Menus");
        names.AddName("Alphinaud", "アルフィノ"); names.AddName("Alphinaud", "アルフィノ（別表記）");
        check(!names.TryResolve("Alphinaud", "", out _) && names.TryResolve("Alphinaud", SourceTextIndex.NamesScope, out mapped) && mapped == "アルフィノ",
            "A name ambiguous across the game resolves from the NPC sheets, where the first original wins");
        names.AddName("ice sprite", "アイススプライト");
        check(names.TryResolve("Ice Sprite", SourceTextIndex.NamesScope, out mapped) && mapped == "アイススプライト", "Names scope also matches case-insensitively");
        var longText = new string('a', 120); names.Add(longText, "長い説明", "Menus");
        check(!names.TryResolve(longText.ToUpperInvariant(), "Menus", out _), "Long descriptions still require an exact match");
        var reloaded = new SourceTextIndex { Ready = true };
        foreach (var record in names.Export()) reloaded.Import(record[0]!, record[1]);
        check(reloaded.TryResolve("Striking Dummy", "Outras janelas", out mapped) && mapped == "木人"
            && reloaded.TryResolve("Alphinaud", SourceTextIndex.NamesScope, out mapped) && mapped == "アルフィノ",
            "Case-insensitive and name lookups survive the saved index round trip");

        // TXT-only mode: a tooltip resolved to its evaluated Japanese original goes to Google,
        // is saved in the TXT and in the separate journal; unmatched client text stays offline.
        var file = new TranslationFile(Path.Combine(scratch, "fallback.txt"));
        var journal = new TranslationJournal(Path.Combine(scratch, "traducoes-novas.txt"));
        var sent = new ConcurrentQueue<(string Language, string Text)>();
        var mainCalls = 0;
        var main = new Handler(_ => { Interlocked.Increment(ref mainCalls); throw new Exception("main provider must stay idle in TXT-only mode"); });
        var index = new SourceTextIndex { Ready = true };
        var live = new Live(new() { ["Delivers an attack with a potency of 380."] = "対象に物理攻撃。　威力：380" });
        using (var primary = new TranslationQueue(file, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", GoogleFallback: false), main)))
        using (var client = new TranslationQueue(file, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", "EN", false), main)))
        using (var googleJa = new TranslationQueue(file, new Translator(new(TranslationProvider.Google, "", false, "", "", "JA", false), Google(sent)), stored: journal.Append))
        using (var googleClient = new TranslationQueue(file, new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false), Google(sent)), stored: journal.Append))
        {
            var router = new TranslationRouter(primary, file, index, true, client, cachedOnly: true, fallback: [new(googleJa, googleClient)], live: live);
            check(router.Get("Delivers an attack with a potency of 380.", true, "Itens e ações").Pending, "A tooltip missing from the TXT is queued on Google in TXT-only mode");
            router.Get("Party leader Some Player", true, "Outras janelas");
            await Until(() => file.TryGet("対象に物理攻撃。　威力：380", out _));
            await Task.Delay(100);
            check(sent.Count == 1 && sent.First() == ("ja", "対象に物理攻撃。　威力：380") && mainCalls == 0,
                "Google translates the evaluated Japanese original, while unmatched client text and the main provider stay offline");
            check(router.Get("Delivers an attack with a potency of 380.", false, "Itens e ações").Text == "PT:対象に物理攻撃。　威力：380",
                "The Google translation is read back from the TXT for the rendered English tooltip");
            var journaled = File.ReadAllLines(journal.Path).Where(x => !x.StartsWith('#')).ToArray();
            check(journaled.SequenceEqual(new[] { "対象に物理攻撃。　威力：380\tPT:対象に物理攻撃。　威力：380" }), "New translations are also journaled separately in the TXT format");
        }

        // A dialogue sentence with embedded names has no static original: it falls back to Google from
        // English. Labels, names and counters elsewhere (quest windows included) stay offline.
        var contentFile = new TranslationFile(Path.Combine(scratch, "fallback-content.txt"));
        var contentSent = new ConcurrentQueue<(string Language, string Text)>();
        using (var primary = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", GoogleFallback: false), main)))
        using (var client = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", "EN", false), main)))
        using (var googleJa = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "JA", false), Google(contentSent))))
        using (var googleClient = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false), Google(contentSent))))
        {
            var router = new TranslationRouter(primary, contentFile, index, true, client, cachedOnly: true, fallback: [new(googleJa, googleClient)]);
            router.Get("Craft 1 Rakshasa Axe and deliver it to Quswib.", true, "Falas");
            router.Get("Party member Some Player", true, "Outras janelas");
            router.Get("Required Class/Job: Warrior", true, "Missões");
            router.Get("Pelupelu Peddler", true, "Falas");
            await Until(() => contentFile.TryGet("Craft 1 Rakshasa Axe and deliver it to Quswib.", out _));
            await Task.Delay(100);
            check(contentSent.SequenceEqual(new[] { ("en", "Craft 1 Rakshasa Axe and deliver it to Quswib.") }) && mainCalls == 0,
                "Only an unmatched dialogue sentence falls back to Google from English; quest labels, names and other windows stay offline");
        }

        // When Google is blocked, a game original kept idle only by TXT-only mode goes to the main provider.
        var blockedFile = new TranslationFile(Path.Combine(scratch, "fallback-blocked.txt"));
        var blockedMain = new ConcurrentQueue<string>();
        var blockedGoogle = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)));
        var blockedDeepL = new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var texts = body.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray();
            foreach (var text in texts) blockedMain.Enqueue(text);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(x => new { text = "DL:" + x }) })) };
        });
        using (var primary = new TranslationQueue(blockedFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", GoogleFallback: false), blockedDeepL)))
        using (var googleJa = new TranslationQueue(blockedFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "JA", false), blockedGoogle, googleCadence: new GoogleCadence())))
        using (var googleClient = new TranslationQueue(blockedFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false), blockedGoogle, googleCadence: new GoogleCadence())))
        {
            var router = new TranslationRouter(primary, blockedFile, index, true, cachedOnly: true, fallback: [new(googleJa, googleClient), new(primary, primary)]);
            router.Get("会話の続き", true, "Falas");
            await Until(() => googleJa.Get("会話の続き", false).Error != null);
            router.Get("会話の続き", true, "Falas");
            await Until(() => blockedFile.TryGet("会話の続き", out _));
            check(blockedMain.SequenceEqual(new[] { "会話の続き" }) && blockedFile.TryGet("会話の続き", out var rescuedLine) && rescuedLine == "DL:会話の続き",
                "A Google block hands a waiting line to the main provider instead of leaving it in English");
        }

        // A failing main provider hands the text to Google instead of leaving it untranslated.
        var failingFile = new TranslationFile(Path.Combine(scratch, "fallback-failure.txt"));
        var failed = new ConcurrentQueue<(string Language, string Text)>();
        var quota = new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)456)));
        using (var primary = new TranslationQueue(failingFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", GoogleFallback: false), quota)))
        using (var googleJa = new TranslationQueue(failingFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "JA", false), Google(failed))))
        using (var googleClient = new TranslationQueue(failingFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false), Google(failed))))
        {
            var router = new TranslationRouter(primary, failingFile, index, true, fallback: [new(primary, primary), new(googleJa, googleClient)]);
            router.Get("失敗する文");
            await Until(() => router.Get("失敗する文", false).Error != null || failingFile.TryGet("失敗する文", out _));
            router.Get("失敗する文");
            await Until(() => failingFile.TryGet("失敗する文", out _));
            check(failed.Single() == ("ja", "失敗する文") && failingFile.TryGet("失敗する文", out var rescued) && rescued == "PT:失敗する文",
                "A main provider failure falls back to Google and saves the result");
        }

        // In TXT-only mode the first provider of the chain (here the main DeepL queue, as with a configured
        // key) translates a missing game original; Google stays unused while it succeeds.
        var chainFile = new TranslationFile(Path.Combine(scratch, "fallback-chain.txt"));
        var chainGoogle = new ConcurrentQueue<(string Language, string Text)>();
        var deepLSent = new ConcurrentQueue<string>();
        var deepL = new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var texts = body.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray();
            foreach (var text in texts) deepLSent.Enqueue(text);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(x => new { text = "DL:" + x }) })) };
        });
        using (var primary = new TranslationQueue(chainFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", GoogleFallback: false), deepL)))
        using (var googleJa = new TranslationQueue(chainFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "JA", false), Google(chainGoogle))))
        using (var googleClient = new TranslationQueue(chainFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false), Google(chainGoogle))))
        {
            var router = new TranslationRouter(primary, chainFile, index, true, cachedOnly: true, fallback: [new(primary, primary), new(googleJa, googleClient)]);
            router.Get("未保存の原文", true, "Menus");
            await Until(() => chainFile.TryGet("未保存の原文", out _));
            await Task.Delay(100);
            check(deepLSent.SequenceEqual(new[] { "未保存の原文" }) && chainGoogle.IsEmpty && chainFile.TryGet("未保存の原文", out var chained) && chained == "DL:未保存の原文",
                "TXT-only mode sends a missing original to the first configured provider of the chain before Google");
        }

        // A redirect to Google's "unusual traffic" page pauses every translator sharing the cadence.
        var cadence = new GoogleCadence();
        using (var blocked = new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false),
            new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found))), googleCadence: cadence))
        {
            var error = "";
            try { await blocked.TranslateAsync(["Speak with Ware Wailer."], CancellationToken.None); }
            catch (TranslationException ex) { error = ex.Message; check(ex.RetryAfter == GoogleCadence.BlockPause, "A Google block schedules the retry after the block pause"); }
            check(error.Contains("bloqueou") && cadence.Wait > 60_000, "A Google redirect is treated as a temporary block that pauses every queue sharing the cadence");
        }

        // Talk lines written into the game's refresh values map back to their source, within a bounded memory.
        var injectedMap = new InjectedTextMap(capacity: 2);
        injectedMap.Add("Olá,\naventureiro!", "Hello, adventurer!");
        check(injectedMap.TryGetOriginal("Olá, aventureiro!", out var injectedOriginal) && injectedOriginal == "Hello, adventurer!"
            && !injectedMap.TryGetOriginal("Olá, viajante!", out _),
            "An injected dialogue line maps back to its source even after the game re-wraps it, and other lines do not");
        injectedMap.Add("Segunda", "Second"); injectedMap.Add("Terceira", "Third");
        check(!injectedMap.TryGetOriginal("Olá, aventureiro!", out _) && injectedMap.TryGetOriginal("Terceira", out _),
            "The injected line memory forgets the oldest entries beyond its capacity");

        // Prefetched quest and tooltip texts resolve the rendered English to the complete Japanese original.
        var prefetchedSources = new PrefetchedSources(capacity: 3);
        prefetchedSources.Add("Craft a plumed iron hatchet\nand deliver it.", "羽飾りの鉄斧を製作して届けよう。");
        prefetchedSources.Add("Sem japonês", "Not Japanese");
        var resolvers = new CompositeSourceResolver(new Live(new() { ["Hovered"] = "ホバー" }), prefetchedSources);
        check(resolvers.TryResolve("Craft a plumed iron hatchet and deliver it.", "Missões", out var prefetchedOriginal) && prefetchedOriginal == "羽飾りの鉄斧を製作して届けよう。"
            && resolvers.TryResolve("Hovered", "", out var hoveredOriginal) && hoveredOriginal == "ホバー"
            && !resolvers.TryResolve("Sem japonês", "", out _) && prefetchedSources.Count == 1,
            "Prefetched texts resolve rendered English (any wrapping) to the complete Japanese original after the hovered tooltip");

        // A Japanese line that pulls an item name from another sheet would lose it ("「」"): it is left out.
        var nameMacro = new byte[] { 2, (byte)Lumina.Text.Payloads.MacroCode.JaNoun, 2, 1, 3 }; // length 1 is encoded as 2
        var withItem = System.Text.Encoding.UTF8.GetBytes("「").Concat(nameMacro).Concat(System.Text.Encoding.UTF8.GetBytes("」を届けて")).ToArray();
        check(BrisaXIV.JapaneseSourceIndex.InsertsNames(new Lumina.Text.ReadOnly.ReadOnlySeString(withItem))
            && !BrisaXIV.JapaneseSourceIndex.InsertsNames(new Lumina.Text.ReadOnly.ReadOnlySeString(System.Text.Encoding.UTF8.GetBytes("鉄の斧を届けて"))),
            "Japanese originals with an inserted item name are recognized as incomplete");

        // A plugin update brings new published entries without replacing the player's corrections.
        var personalPath = Path.Combine(scratch, "personal.txt");
        var personal = new TranslationFile(personalPath); personal.Store("既存", "Correção pessoal");
        var package = Path.Combine(scratch, "package.txt");
        File.WriteAllText(package, "# pacote\n既存\tPacote\n新規\tNovo\n新規\tNovo corrigido\n");
        var imported = personal.ImportMissing(package);
        var reopened = new TranslationFile(personalPath);
        check(imported == 1 && reopened.TryGet("既存", out var kept) && kept == "Correção pessoal"
            && reopened.TryGet("新規", out var added) && added == "Novo corrigido" && personal.ImportMissing(package) == 0,
            "Package updates add only missing originals, keep personal corrections, and are idempotent");
    }
}
