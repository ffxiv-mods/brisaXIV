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
            var router = new TranslationRouter(primary, file, index, true, client, cachedOnly: true, fallback: new(googleJa, googleClient), live: live);
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

        // Quest journal text with embedded item or character names has no static original: in game
        // content it falls back to Google from English, while other windows stay offline.
        var contentFile = new TranslationFile(Path.Combine(scratch, "fallback-content.txt"));
        var contentSent = new ConcurrentQueue<(string Language, string Text)>();
        using (var primary = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", GoogleFallback: false), main)))
        using (var client = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", "EN", false), main)))
        using (var googleJa = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "JA", false), Google(contentSent))))
        using (var googleClient = new TranslationQueue(contentFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false), Google(contentSent))))
        {
            var router = new TranslationRouter(primary, contentFile, index, true, client, cachedOnly: true, fallback: new(googleJa, googleClient));
            router.Get("Craft 1 Rakshasa Axe and deliver it to Quswib.", true, "Missões");
            router.Get("Party member Some Player", true, "Outras janelas");
            await Until(() => contentFile.TryGet("Craft 1 Rakshasa Axe and deliver it to Quswib.", out _));
            await Task.Delay(100);
            check(contentSent.SequenceEqual(new[] { ("en", "Craft 1 Rakshasa Axe and deliver it to Quswib.") }) && mainCalls == 0,
                "Unmatched quest journal text falls back to Google from English, while other HUD windows stay offline");
        }

        // A failing main provider hands the text to Google instead of leaving it untranslated.
        var failingFile = new TranslationFile(Path.Combine(scratch, "fallback-failure.txt"));
        var failed = new ConcurrentQueue<(string Language, string Text)>();
        var quota = new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)456)));
        using (var primary = new TranslationQueue(failingFile, new Translator(new(TranslationProvider.DeepL, "key", false, "", "", GoogleFallback: false), quota)))
        using (var googleJa = new TranslationQueue(failingFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "JA", false), Google(failed))))
        using (var googleClient = new TranslationQueue(failingFile, new Translator(new(TranslationProvider.Google, "", false, "", "", "EN", false), Google(failed))))
        {
            var router = new TranslationRouter(primary, failingFile, index, true, fallback: new(googleJa, googleClient));
            router.Get("失敗する文");
            await Until(() => router.Get("失敗する文", false).Error != null || failingFile.TryGet("失敗する文", out _));
            router.Get("失敗する文");
            await Until(() => failingFile.TryGet("失敗する文", out _));
            check(failed.Single() == ("ja", "失敗する文") && failingFile.TryGet("失敗する文", out var rescued) && rescued == "PT:失敗する文",
                "A main provider failure falls back to Google and saves the result");
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
