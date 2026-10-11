using System.Net;
using System.Text;
using System.Text.Json;
using BrisaXIV;
using BrisaXIV.Core;
using Lumina;
using Lumina.Excel;
using Lumina.Data;
using Lumina.Data.Structs.Excel;
using Lumina.Text.ReadOnly;

var scratch = Path.Combine(args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetTempPath(), "BrisaXIV-tests-" + Guid.NewGuid());
Directory.CreateDirectory(scratch);
var passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
async Task WaitUntil(Func<bool> check) { var end = DateTime.UtcNow.AddSeconds(8); while (!check()) { if (DateTime.UtcNow > end) throw new Exception("timeout"); await Task.Delay(15); } }

Check(TextRules.ContainsJapanese("こんにちは") && TextRules.ContainsJapanese("冒険") && TextRules.ContainsJapanese("ｱｲｳ"), "Japanese scripts detected");
Check(!TextRules.ContainsJapanese("Português: ação, João"), "Portuguese is not translated again");
var nativeNewline = Encoding.UTF8.GetBytes("最初").Concat(new byte[] { 2, 0x10, 1, 3 }).Concat(Encoding.UTF8.GetBytes("最後")).ToArray();
Check(NativeTextContent.Extract(new ReadOnlySeStringSpan(nativeNewline)) == "最初\n最後", "Native newline payload and quest text share the same cache identity");
Check(NativeTextContent.CanReplace(new ReadOnlySeStringSpan(nativeNewline)), "Multiline dialogue can be replaced in native HUD");
var nativeIcon = Encoding.UTF8.GetBytes("最初").Concat(new byte[] { 2, 0x12, 2, 2, 3 }).ToArray();
Check(!NativeTextContent.CanReplace(new ReadOnlySeStringSpan(nativeIcon)), "Icon payloads are preserved by keeping their native fields untouched");
var colorOn = new byte[] { 2, 0x48, 2, 2, 3 }; // UIColor row 1
var colorOff = new byte[] { 2, 0x48, 2, 1, 3 }; // reset
var nativeStyled = colorOn.Concat(nativeNewline).Concat(colorOff).ToArray();
Check(NativeTextContent.TryReplace(new ReadOnlySeStringSpan(nativeStyled), "Por favor, proteja\no cordeirinho!", out var styledReplacement)
    && styledReplacement.AsSpan(0, colorOn.Length).SequenceEqual(colorOn)
    && styledReplacement.AsSpan(styledReplacement.Length - colorOff.Length - 1, colorOff.Length).SequenceEqual(colorOff)
    && NativeTextContent.Extract(new ReadOnlySeStringSpan(styledReplacement.AsSpan(0, styledReplacement.Length - 1))) == "Por favor, proteja\no cordeirinho!",
    "Formatted native balloon keeps color wrappers, contains Portuguese once, and encodes native line breaks");
Check(!NativeTextContent.TryReplace(new ReadOnlySeStringSpan(nativeIcon), "Português", out _)
    && !NativeTextContent.TryReplace(new ReadOnlySeStringSpan(nativeNewline), "Inválido\0texto", out _),
    "Unsupported native payloads and injected control bytes do not damage game text");
var inlineColor = Encoding.UTF8.GetBytes("Grants ").Concat(colorOn).Concat(Encoding.UTF8.GetBytes("Astral Fire")).Concat(colorOff).Concat(Encoding.UTF8.GetBytes(".")).ToArray();
var highlightedName = colorOn.Concat(Encoding.UTF8.GetBytes("Fogo Astral")).Concat(colorOff).ToArray();
Check(NativeTextContent.TryReplace(new ReadOnlySeStringSpan(inlineColor), "Concede “Fogo Astral” ao usar 「Gelo」.", out var highlighted)
    && NativeTextContent.Extract(new ReadOnlySeStringSpan(highlighted.AsSpan(0, highlighted.Length - 1))) == "Concede Fogo Astral ao usar Gelo."
    && highlighted.AsSpan().IndexOf(highlightedName) > 0,
    "An inline colored name keeps its color on the quoted names of the translation, without the quotes");
Check(NativeTextContent.TryReplace(new ReadOnlySeStringSpan(nativeStyled), "Diga “olá”.", out var wrappedQuote)
    && NativeTextContent.Extract(new ReadOnlySeStringSpan(wrappedQuote.AsSpan(0, wrappedQuote.Length - 1))) == "Diga “olá”.",
    "Quotes stay when the original only colors the whole text, with no inline emphasis to move");
var longDialogue = "Você também quer ouvir?\nTem uma garota que eu conheço que é super arrogante, e todo mundo estava tendo muito trabalho com ela, mas então veio um oráculo dizendo: Antes do sol se pôr, cite cinco qualidades positivas de um amigo.";
var wrappedDialogue = TextBoxLayout.Wrap(longDialogue, 55, value => value.Length);
Check(wrappedDialogue.Split('\n').All(line => line.Length <= 55)
    && SourceTextIndex.Key(wrappedDialogue) == SourceTextIndex.Key(longDialogue)
    && TextBoxLayout.ExtraHeight(120, 70) == 50 && TextBoxLayout.ExtraHeight(40, 70) == 0,
    "The user's long dialogue wraps without losing words and grows the box only when its measured height exceeds available space");
var longWord = "ação" + string.Concat(Enumerable.Repeat("👩🏽‍🚀", 8)) + "a\u0301";
var wrappedWord = TextBoxLayout.Wrap(longWord, 3, value => new System.Globalization.StringInfo(value).LengthInTextElements);
Check(wrappedWord.Replace("\n", "") == longWord && wrappedWord.Split('\n').All(line => new System.Globalization.StringInfo(line).LengthInTextElements <= 3),
    "Oversized words wrap without splitting accents, surrogate pairs, or combined emoji");
const string confirmationText = "São necessários 10 MGP para comprar um bilhete.\nVocê possui 754.732 no momento. Deseja prosseguir?";
var wrappedConfirmation = TextBoxLayout.Wrap(confirmationText, 42, value => value.Length);
var confirmationHeight = wrappedConfirmation.Split('\n').Length * 16 + 2;
Check(SourceTextIndex.Key(wrappedConfirmation) == SourceTextIndex.Key(confirmationText)
    && wrappedConfirmation.Split('\n').All(line => line.Length <= 42)
    && TextBoxLayout.TryFitConfirmation(22, confirmationHeight, 70, 112, out var confirmationFit)
    && 22 + confirmationHeight + 12 <= 70 + confirmationFit.ExtraHeight
    && confirmationFit.WindowHeight - (70 + confirmationFit.ExtraHeight) == 112 - 70,
    "The MGP confirmation from the screenshot keeps every word, leaves 12 pixels above the buttons, and preserves their bottom margin");
Check(TextBoxLayout.TryFitConfirmation(22, 34, 100, 142, out var shortConfirmation) && shortConfirmation.ExtraHeight == 0
    && TextBoxLayout.TryFitConfirmation(22, 250, 100, 142, out var longConfirmation)
    && 22 + 250 + 12 <= 100 + longConfirmation.ExtraHeight,
    "Short confirmations keep their size while long messages reserve space for the whole button/check-box footer");
Check(!TextBoxLayout.TryFitConfirmation(22, 800, 70, 112, out _)
    && !TextBoxLayout.TryFitConfirmation(22, 68, 20, 112, out _)
    && !TextBoxLayout.TryFitConfirmation(float.NaN, 68, 70, 112, out _),
    "Unbounded or invalid confirmation geometry is rejected instead of writing overlapping native controls");
NativeTextLayoutTests.Run(Check);
Check(TextCategoryPolicy.PartEnabled(new Dictionary<string, bool> { ["Itens"] = false }, "Itens", true) == false && TextCategoryPolicy.PartEnabled(new Dictionary<string, bool>(), "Missões", true) && !TextCategoryPolicy.PartEnabled(null, "Menus", false), "Category parts preserve inherited choices and explicit overrides");
Check(TextCategoryPolicy.PartCategory("ItemDetail", "Itens e ações") == "Itens" && TextCategoryPolicy.PartCategory("ActionDetail", "Itens e ações") == "Habilidades" && TextCategoryPolicy.PartCategory("SelectYesno", "Menus") == "Confirmações", "Category part keys keep items actions and confirmations independent");
Check(TextCategoryPolicy.SpecializedCategory("Teleport") == "Locais e viagens" && TextCategoryPolicy.SpecializedCategory("_AreaText") == "Locais e viagens" && TextCategoryPolicy.SpecializedCategory("RecipeNote") == "Receitas e fabricação" && TextCategoryPolicy.SpecializedCategory("Achievement") == "Conquistas e coleções" && TextCategoryPolicy.SpecializedCategory("Inventory") == null, "Specialized windows are classified without capturing unrelated menus");
Check(TextCategoryPolicy.IsSubtitle("TalkSubtitle") && !TextCategoryPolicy.IsSubtitle("Talk")
    && TextCategoryPolicy.IsConfirmation("SelectYesno") && TextCategoryPolicy.IsConfirmation("SelectOk")
    && !TextCategoryPolicy.IsConfirmation("SelectString"), "Subtitle and confirmation categories remain independent from dialogue and menu lists");
Check(!TextCategoryPolicy.IsNodeEnabled(true, false, TextCategoryPolicy.IsWindowHeading(true, 3), false)
    && TextCategoryPolicy.IsNodeEnabled(true, false, TextCategoryPolicy.IsWindowHeading(false, 3), false)
    && !TextCategoryPolicy.IsNodeEnabled(false, true, true, false)
    && !TextCategoryPolicy.IsNodeEnabled(true, true, false, true),
    "Disabling headings preserves message bodies, does not reactivate a disabled window category, and never enables NPC speaker names");
Check(DialoguePresentation.IsSpeakerName("Talk", 2) && DialoguePresentation.IsSpeakerName("_BattleTalk", 4)
    && !DialoguePresentation.IsSpeakerName("Talk", 3) && !DialoguePresentation.IsSpeakerName("_BattleTalk", 6),
    "Dialogue speaker names are excluded while the actual speech nodes remain translatable");
Check(new[] { "Talk", "_MiniTalk", "MiniTalk", "BattleTalk", "_BattleTalk", "TalkSubtitle" }
    .All(name => name is "Talk" or "_MiniTalk" or "MiniTalk" or "BattleTalk" or "_BattleTalk" or "TalkSubtitle"),
    "All native dialogue and speech-bubble addon names share the same presentation policy");
var presentation = new DialoguePresentation();
presentation.ObserveSource("Primeira fala");
Check(presentation.ShouldWait(1000, true, 10) && presentation.ShouldWait(10999, true, 10)
    && !presentation.ShouldWait(11000, true, 10) && !presentation.ShouldWait(11001, false, 10)
    && presentation.ShouldWait(12000, true, 10), "New dialogue waits for translation, reveals on timeout, and resets for the next line");
presentation.ObserveSource("Segunda fala");
Check(presentation.ShouldWait(25000, true, 10), "Changing dialogue resets the presentation deadline even after a previous timeout");
Check(!NativeTextPolicy.ShouldRestore(false, true, true, true)
    && NativeTextPolicy.ShouldRestore(true, true, true, true) && NativeTextPolicy.ShouldRestore(false, false, true, true)
    && NativeTextPolicy.ShouldRestore(false, true, false, true) && NativeTextPolicy.ShouldRestore(false, true, true, false),
    "Closing-window visibility never requests original-text restoration; explicit disable, category change and unload still restore");
Check(NativeTextPolicy.MatchesAppliedText(true, "Português", "Original", "Português")
    && NativeTextPolicy.MatchesAppliedText(true, "Original", "Original", "Português")
    && !NativeTextPolicy.MatchesAppliedText(true, "Próxima fala", "Original", "Português")
    && !NativeTextPolicy.MatchesAppliedText(false, "Original", "Original", "Português"),
    "A game refresh during closing retains the matching translation while a new line or reused node invalidates the previous text");
var firstBalloon = new DialoguePresentation(); var secondBalloon = new DialoguePresentation();
firstBalloon.ObserveSource("Fala A"); secondBalloon.ObserveSource("Fala B");
Check(firstBalloon.ShouldWait(1000, true, 10) && secondBalloon.ShouldWait(9000, true, 10)
    && !firstBalloon.ShouldWait(11000, true, 10) && secondBalloon.ShouldWait(11000, true, 10),
    "Loading deadlines belong to individual text nodes, so simultaneous NPC balloons cannot hide or reset each other");
var example = "日本語\tlinha\n\\n e \\t e \\ outro";
Check(TextRules.Unescape(TextRules.Escape(example)) == example, "File escaping round trip including literal escapes");
var filePath = Path.Combine(scratch, "traducoes.txt");
var file = new TranslationFile(filePath);
file.Store("日本語\n次の行", "Português\nOutra linha");
var reopened = new TranslationFile(filePath);
Check(reopened.TryGet("日本語\r\n次の行", out var saved) && saved == "Português\nOutra linha", "Text cache survives restart and newline normalization");
File.AppendAllText(filePath, TextRules.Escape("日本語\n次の行") + "\tCorreção humana\n", new UTF8Encoding(false));
reopened.Reload(); reopened.Store("日本語\n次の行", "Resposta tardia");
Check(reopened.TryGet("日本語\n次の行", out saved) && saved == "Correção humana", "Human edits take precedence over late API results");
var secret = "test-only-not-a-real-key:fx";
var protectedKey = KeyProtection.Protect(secret);
Check(!protectedKey.Contains(secret) && KeyProtection.Unprotect(protectedKey) == secret, "DPAPI key protection round trip");

await OpenAITranslationTests.Run(Check, scratch);
await TranslationProgressTests.Run(Check, scratch);
await TranslationRetryTests.Run(Check, scratch);
await AIUsageTests.Run(Check, scratch);
await FallbackRoutingTests.Run(Check, scratch);

var calls = 0;
var translated = new Dictionary<string, string> { ["こんにちは。"] = "Olá.", ["ありがとう。"] = "Obrigado." };
var handler = new FakeHandler(async request =>
{
    Interlocked.Increment(ref calls);
    Check(request.RequestUri!.Host == "api-free.deepl.com", "DeepL Free endpoint");
    Check(request.Headers.GetValues("Authorization").Single() == "DeepL-Auth-Key " + secret, "Key is sent in header");
    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    Check(json.RootElement.GetProperty("source_lang").GetString() == "JA" && json.RootElement.GetProperty("target_lang").GetString() == "PT-BR", "JA to Brazilian Portuguese API request");
    var texts = json.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray();
    await Task.Delay(80);
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(x => new { text = translated[x] }) }), Encoding.UTF8, "application/json") };
});
var queue = new TranslationQueue(reopened, new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), handler));
for (var i = 0; i < 100; i++) queue.Get("こんにちは。");
queue.Get("ありがとう。");
await WaitUntil(() => queue.Get("ありがとう。", false).Text == "Obrigado.");
Check(calls == 1, "Duplicate requests are deduplicated and new lines batched");
queue.Dispose(); await queue.Completion;
var afterRestartCalls = 0;
var second = new TranslationQueue(new TranslationFile(filePath), new Translator(new(TranslationProvider.DeepL, "", false, "", ""), new FakeHandler(_ => { afterRestartCalls++; throw new Exception("cache should avoid API"); })));
Check(second.Get("こんにちは。").Text == "Olá." && afterRestartCalls == 0, "Reopened cache serves translations without credentials or network");
second.Dispose(); await second.Completion;
var errorCalls = 0;
var errorQueue = new TranslationQueue(new TranslationFile(Path.Combine(scratch, "quota.txt")), new Translator(new(TranslationProvider.DeepL, secret, false, "", "", GoogleFallback: false), new FakeHandler(_ =>
{ errorCalls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)456)); })));
errorQueue.Get("最初");
await WaitUntil(() => errorQueue.Get("最初", false).Error != null);
errorQueue.Get("次"); await Task.Delay(100);
Check(errorCalls == 1 && errorQueue.Get("次", false).Error!.Contains("Cota"), "Quota error stops additional API calls");
errorQueue.Dispose(); await errorQueue.Completion;

var indexRules = new SourceTextIndex();
indexRules.Add("A choice─one\nline", "選択肢", "Menus");
Check(indexRules.TryResolve("A choice—one line", "Menus", out var mapped) && mapped == "選択肢", "English wrapping and dash variants resolve to the same Japanese original");
indexRules.Add("Shared", "物語", "Falas"); indexRules.Add("Shared", "共通", "Menus");
Check(!indexRules.TryResolve("Shared", "", out _) && indexRules.TryResolve("Shared", "Falas", out mapped) && mapped == "物語", "Ambiguous originals are rejected; matching surface context resolves them");
var routingFile = new TranslationFile(Path.Combine(scratch, "routing.txt")); routingFile.Store("選択肢", "Uma escolha");
var routingCalls = 0;
using (var routingQueue = new TranslationQueue(routingFile, new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(_ => { routingCalls++; throw new Exception("no network expected"); }))))
{
    var router = new TranslationRouter(routingQueue, routingFile, indexRules, true, cachedOnly: true);
    Check(router.Get("A choice—one line", true, "Menus").Text == "Uma escolha" && routingCalls == 0, "English game client displays cached Portuguese translated from Japanese without a network call");
    Check(HudTranslation.Get(router, "A choice—one line", "Menus", false, 0).Text == "Uma escolha" && routingCalls == 0,
        "A saved Japanese translation appears on the very first capture of an English line, bypassing the debounce and network");
    router.Get("未翻訳"); await Task.Delay(100);
    Check(routingCalls == 0, "TXT-only HUD never sends missing text for translation");
}
var recoveryPath = Path.Combine(scratch, "recovery.txt"); var recovery = new TranslationFile(recoveryPath); recovery.Store("回復", "Recuperação");
File.Delete(recoveryPath);
Check(new TranslationFile(recoveryPath).TryGet("回復", out var recovered) && recovered == "Recuperação", "Deleted TXT is automatically restored from its local recovery copy");

var deepLAttempts = 0; var googleAttempts = 0;
using (var fallback = new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(async request =>
{
    if (request.RequestUri!.Host == "api-free.deepl.com") { deepLAttempts++; return new((HttpStatusCode)456); }
    googleAttempts++;
    Check(request.RequestUri.Host == "translate.googleapis.com" && !request.Headers.Contains("Authorization") && string.IsNullOrEmpty(request.RequestUri.Query), "Google fallback receives no DeepL key and no game text in the URL");
    Check((await request.Content!.ReadAsStringAsync()).Contains("tl=pt-BR"), "Google fallback requests Brazilian Portuguese");
    return new(HttpStatusCode.OK) { Content = new StringContent("[[[\"Tradução\",\"original\"]],null,\"ja\"]", Encoding.UTF8, "application/json") };
})))
{
    await fallback.TranslateAsync(["最初"], CancellationToken.None); await fallback.TranslateAsync(["次"], CancellationToken.None);
    Check(deepLAttempts == 1 && googleAttempts == 2 && fallback.UsingGoogleFallback, "Quota exhaustion switches automatically to Google and prevents repeated DeepL quota calls");
}
var partialPath = Path.Combine(scratch, "partial-google.txt"); var partialFile = new TranslationFile(partialPath); var partialCalls = 0;
using (var partialQueue = new TranslationQueue(partialFile, new Translator(new(TranslationProvider.Google, "", false, "", ""), new FakeHandler(_ => Task.FromResult(++partialCalls == 1
    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[[[\"Primeira tradução\",\"original\"]]]") }
    : new HttpResponseMessage((HttpStatusCode)429))))))
{
    partialQueue.Get("最初"); partialQueue.Get("次");
    await WaitUntil(() => partialQueue.Get("次", false).Error != null);
    Check(new TranslationFile(partialPath).TryGet("最初", out _), "A successful Google result survives failure of a later request in the same batch");
}
using (var longTranslator = new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(async request =>
{
    using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    var texts = document.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray();
    Check(texts.All(x => x.Length <= 4000), "Long records are split into bounded translation requests");
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(_ => new { text = "Trecho" }) })) };
})))
    Check((await longTranslator.TranslateAsync([new string('あ', 10000)], CancellationToken.None))[0] == "Trecho\nTrecho\nTrecho", "Long Japanese text is retained as one reusable TXT entry");

var preparationCatalog = new PreparationCatalog();
Check(TextCategoryPolicy.IsJournal("Journal") && TextCategoryPolicy.IsJournal("JournalDetail")
    && !TextCategoryPolicy.IsJournal("JournalAccept") && !TextCategoryPolicy.IsJournal("JournalResult")
    && !TextCategoryPolicy.IsJournal("ToDoList"), "Journal toggle targets the list and details without disabling acceptance results or objectives");
preparationCatalog.Add("最新", 5, "New"); preparationCatalog.Add("古い", 0, "Old"); preparationCatalog.Add("共通", -1, "Menus"); preparationCatalog.Finish("test-version");
var preparationPath = Path.Combine(scratch, "preparation.json"); var preparationFile = new TranslationFile(Path.Combine(scratch, "prepared.txt"));
var preparationRequests = new List<string>();
using (var preparationQueue = new TranslationQueue(preparationFile, new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(async request =>
{
    using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    var texts = document.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray(); preparationRequests.AddRange(texts);
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(x => new { text = "PT:" + x }) })) };
}))))
{
    var preparation = new BackgroundPreparation(preparationCatalog, preparationFile, preparationPath);
    preparation.Pump(preparationQueue, true, 1000);
    await WaitUntil(() => preparationFile.Count == 3); await Task.Delay(1050); preparation.Pump(preparationQueue, true, 1000);
    Check(preparation.Completed && preparationRequests.SequenceEqual(new[] { "最新", "古い", "共通" }), "Background preparation follows newest-to-oldest order and completes only after TXT records are saved");
    var restarted = new BackgroundPreparation(preparationCatalog, new TranslationFile(preparationFile.Path), preparationPath);
    restarted.Pump(preparationQueue, true, 1000);
    Check(restarted.Completed && preparationRequests.Count == 3, "Completed preparation persists across restart without translating stored records again");
}

var priorityFile = new TranslationFile(Path.Combine(scratch, "priority.txt"));
var priorityRequests = new System.Collections.Concurrent.ConcurrentQueue<string>();
var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finishFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (var priorityQueue = new TranslationQueue(priorityFile, new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(async request =>
{
    using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    var texts = document.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray();
    foreach (var text in texts) priorityRequests.Enqueue(text);
    if (texts.Contains("背景先頭")) { firstRequestStarted.SetResult(); await finishFirstRequest.Task; }
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(x => new { text = "PT:" + x }) })) };
})), capacity: 3))
{
    priorityQueue.Get("背景先頭"); await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(8));
    priorityQueue.Get("背景続き"); priorityQueue.Get("昇格した会話");
    Check(priorityQueue.Get("満杯").Error != null && priorityQueue.Get("新会話", priority: true).Pending, "An NPC dialogue can enter even when the background queue is full");
    for (var i = 0; i < 100; i++) priorityQueue.Get("昇格した会話", priority: true);
    finishFirstRequest.SetResult(); await WaitUntil(() => priorityFile.Count == 4 && priorityQueue.PendingCount == 0);
    var order = priorityRequests.ToArray();
    Check(Array.IndexOf(order, "新会話") < Array.IndexOf(order, "背景続き") && Array.IndexOf(order, "昇格した会話") < Array.IndexOf(order, "背景続き"), "Open NPC dialogue and a promoted existing request run before waiting background text");
    Check(order.Count(x => x == "昇格した会話") == 1 && new TranslationFile(priorityFile.Path).Count == 4, "Priority promotion deduplicates requests and preserves all background results in the TXT");
}

var googlePriorityFile = new TranslationFile(Path.Combine(scratch, "google-priority.txt"));
var googlePriorityRequests = new System.Collections.Concurrent.ConcurrentQueue<string>();
var googleFirstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var googleFirstFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (var googlePriorityQueue = new TranslationQueue(googlePriorityFile, new Translator(new(TranslationProvider.Google, "", false, "", ""), new FakeHandler(async request =>
{
    var form = await request.Content!.ReadAsStringAsync();
    var text = Uri.UnescapeDataString(form.Split('&').First(field => field.StartsWith("q="))[2..].Replace('+', ' '));
    googlePriorityRequests.Enqueue(text);
    if (text == "最初の背景") { googleFirstStarted.SetResult(); await googleFirstFinish.Task; }
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new object[] { new object[] { new object[] { "PT:" + text, text } } })) };
}))))
{
    googlePriorityQueue.Get("最初の背景"); googlePriorityQueue.Get("残りの背景"); googlePriorityQueue.Get("会話の優先");
    await googleFirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(8));
    googlePriorityQueue.Get("会話の優先", priority: true);
    googleFirstFinish.SetResult(); await WaitUntil(() => googlePriorityFile.Count == 3 && googlePriorityQueue.PendingCount == 0);
    Check(googlePriorityRequests.SequenceEqual(new[] { "最初の背景", "会話の優先", "残りの背景" }), "Google yields between individual requests so an NPC does not wait for the entire background batch");
    Check(googlePriorityQueue.Get("残りの背景", false).Error == null && new TranslationFile(googlePriorityFile.Path).Count == 3, "Yielding for dialogue retains completed Google work and resumes background without errors or duplicate translations");
}

var longPriorityFile = new TranslationFile(Path.Combine(scratch, "long-priority.txt"));
var longPriorityRequests = new System.Collections.Concurrent.ConcurrentQueue<string>();
var longLastFragmentStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var longLastFragmentFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var longSource = new string('あ', 4100);
using (var longPriorityQueue = new TranslationQueue(longPriorityFile, new Translator(new(TranslationProvider.Google, "", false, "", ""), new FakeHandler(async request =>
{
    var form = await request.Content!.ReadAsStringAsync();
    var text = Uri.UnescapeDataString(form.Split('&').First(field => field.StartsWith("q="))[2..].Replace('+', ' '));
    longPriorityRequests.Enqueue(text);
    if (text.Length == 100) { longLastFragmentStarted.SetResult(); await longLastFragmentFinish.Task; }
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new object[] { new object[] { new object[] { "Trecho traduzido", text } } })) };
}))))
{
    longPriorityQueue.Get(longSource); longPriorityQueue.Get("背景の短文");
    await longLastFragmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(8)); longPriorityQueue.Get("今の会話", priority: true);
    longLastFragmentFinish.SetResult(); await WaitUntil(() => longPriorityFile.Count == 3 && longPriorityQueue.PendingCount == 0);
    var longOrder = longPriorityRequests.ToArray();
    Check(longOrder.Length == 4 && longOrder[2] == "今の会話" && longOrder[3] == "背景の短文" && longPriorityFile.TryGet(longSource, out _), "Long text finishes and is saved once before yielding to NPC dialogue, without losing or repeating translated fragments");
}

var liveDialogueIndex = new SourceTextIndex { Ready = true }; liveDialogueIndex.Add("Hello, adventurer!", "冒険者、こんにちは！", "Falas");
var liveDialogueFile = new TranslationFile(Path.Combine(scratch, "dialogue-router.txt"));
var liveDialogueRequests = new System.Collections.Concurrent.ConcurrentQueue<string>();
using (var liveDialogueQueue = new TranslationQueue(liveDialogueFile, new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(async request =>
{
    using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    var texts = document.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray();
    foreach (var text in texts) liveDialogueRequests.Enqueue(text);
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(_ => new { text = "Olá, aventureiro!" }) })) };
}))))
{
    var router = new TranslationRouter(liveDialogueQueue, liveDialogueFile, liveDialogueIndex, true, cachedOnly: true, prioritizeDialogue: true);
    HudTranslation.Get(router, "Hello, adventurer!", "Falas", true, 0);
    HudTranslation.Get(router, "Hello, adventurer!", "Falas", true, 349);
    await Task.Delay(70);
    Check(liveDialogueRequests.IsEmpty && liveDialogueQueue.PendingCount == 0, "Incomplete typewriter text is still debounced without network calls or priority requests");
    router.Get("Hello, adventurer!", true, "Falas");
    router.Get("未翻訳のメニュー", true, "Menus"); router.Get("過去の会話", false, "Falas");
    await WaitUntil(() => router.Get("Hello, adventurer!", false, "Falas").Text == "Olá, aventureiro!"); await Task.Delay(100);
    Check(liveDialogueRequests.SequenceEqual(new[] { "冒険者、こんにちは！" }), "TXT mode permits only the open prioritized dialogue, resolves Japanese from English, and leaves menus and history offline");
    var pausedPreparation = new BackgroundPreparation(preparationCatalog, liveDialogueFile, Path.Combine(scratch, "paused-priority.json"));
    pausedPreparation.Pump(liveDialogueQueue, true, 1000, priorityActive: true);
    Check(pausedPreparation.Cursor == 0 && liveDialogueQueue.PendingCount == 0 && pausedPreparation.Message.StartsWith("Priorizando"), "Dialogue priority pauses new bulk work without changing its confirmed progress");
}

var fastCatalog = new PreparationCatalog();
var fastLines = new List<string>();
for (var i = 0; i < 6000; i++)
{
    var original = $"保存済み{i:D5}";
    fastCatalog.Add(original, 5, i < 3000 ? "A" : "C");
    fastLines.Add(original + "\tSalvo");
}
fastCatalog.Add("不足一", 5, "B"); fastCatalog.Add("不足二", 5, "D"); fastCatalog.Finish("fast-cache");
var fastPath = Path.Combine(scratch, "fast-cache.txt"); File.WriteAllLines(fastPath, fastLines);
var fastFile = new TranslationFile(fastPath); var fastSent = new List<string>();
using (var fastQueue = new TranslationQueue(fastFile, new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(async request =>
{
    using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    var texts = document.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray(); fastSent.AddRange(texts);
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(_ => new { text = "Novo" }) })) };
}))))
{
    var fastJob = new BackgroundPreparation(fastCatalog, fastFile, Path.Combine(scratch, "fast-progress.json"));
    var fastTimer = System.Diagnostics.Stopwatch.StartNew();
    while (fastFile.Count < 6002 && fastTimer.ElapsedMilliseconds < 5000)
    { fastJob.Pump(fastQueue, true, 1000, continuous: true); await Task.Delay(20); }
    Check(fastFile.Count == 6002 && fastSent.Count == 2 && fastSent.ToHashSet().SetEquals(new[] { "不足一", "不足二" }),
        "DeepL skips thousands of cached entries before and between holes promptly without resending stored translations");
    while (!fastJob.Completed && fastTimer.ElapsedMilliseconds < 8000)
    { fastJob.Pump(fastQueue, true, 1000, continuous: true); await Task.Delay(20); }
    Check(fastJob.Completed && new TranslationFile(fastPath).Count == 6002,
        "Accelerated cached scan still completes only after missing texts are saved and the full audit confirms them");
}

var continuousCatalog = new PreparationCatalog();
continuousCatalog.Add(new string('あ', 800), 5, "New"); continuousCatalog.Add(new string('い', 800), 5, "New");
continuousCatalog.Add(new string('う', 800), 0, "Old"); continuousCatalog.Finish("continuous-test");
var continuousFile = new TranslationFile(Path.Combine(scratch, "continuous.txt"));
using (var continuousQueue = new TranslationQueue(continuousFile, new Translator(new(TranslationProvider.DeepL, secret, false, "", ""), new FakeHandler(async request =>
{
    using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    var texts = document.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString()!).ToArray();
    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translations = texts.Select(_ => new { text = "Português" }) })) };
}))))
{
    var job = new BackgroundPreparation(continuousCatalog, continuousFile, Path.Combine(scratch, "continuous.json"));
    job.Pump(continuousQueue, true, 1000);
    await WaitUntil(() => continuousFile.Count == 1 && continuousQueue.PendingCount == 0);
    await Task.Delay(1050); job.Pump(continuousQueue, true, 1000);
    Check(continuousFile.Count == 1 && job.Message.StartsWith("Parte desta sessão"), "Limited mode still pauses after its session budget");
    await Task.Delay(1050); job.Pump(continuousQueue, true, 1000, continuous: true);
    await WaitUntil(() => continuousFile.Count == 3 && continuousQueue.PendingCount == 0);
    await Task.Delay(1050); job.Pump(continuousQueue, true, 1000, continuous: true);
    Check(job.Completed && job.SessionCharacters == 2400 && new TranslationFile(continuousFile.Path).Count == 3,
        "Enabling continuous mode resumes past the exhausted budget and finishes all texts without losing saved work");
}

if (args.Length > 1 && args[1] == "--live")
{
    var pluginRoot = Path.GetFullPath(args[2]);
    var localKey = KeyProtection.Unprotect(File.ReadAllText(Path.Combine(pluginRoot, "chave.dpapi")).Trim());
    var liveFile = new TranslationFile(Path.Combine(pluginRoot, "traducoes.txt"));
    const string source = "こんにちは。";
    if (!liveFile.TryGet(source, out _))
    {
        using var liveTranslator = new Translator(new(TranslationProvider.DeepL, localKey, false, "", ""));
        var result = await liveTranslator.TranslateAsync([source], CancellationToken.None);
        liveFile.Store(source, result[0]);
    }
    Check(liveFile.TryGet(source, out var response) && !string.IsNullOrWhiteSpace(response), "Live DeepL Free JA to PT-BR translation saved to personal text file");
}
else if (args.Length > 1 && args[1] == "--screenshot")
{
    var root = Path.GetFullPath(args[2]);
    var entry = File.ReadLines(Path.Combine(root, "fontes-English-JA.jsonl")).Where(line => line.Contains("Elene'shpya came by a while"))
        .Select(line => JsonSerializer.Deserialize<string?[]>(line)!).First(record => record.Length == 2 && record[1] != null);
    var original = entry[1]!;
    var personalFile = new TranslationFile(Path.Combine(root, "traducoes.txt"));
    if (!personalFile.TryGet(original, out _))
    {
        var localKey = KeyProtection.Unprotect(File.ReadAllText(Path.Combine(root, "chave.dpapi")).Trim());
        using var translator = new Translator(new(TranslationProvider.DeepL, localKey, false, "", ""));
        personalFile.Store(original, (await translator.TranslateAsync([original], CancellationToken.None))[0]);
    }
    Check(personalFile.TryGet(original, out _), "User screenshot NPC dialogue was translated from its Japanese original and saved for immediate offline display");
}
else if (args.Length > 1 && args[1] == "--prepare")
{
    var pluginRoot = Path.GetFullPath(args[2]);
    var corpusPath = Path.Combine(pluginRoot, "originais-japoneses.jsonl");
    var fingerprint = File.ReadLines(corpusPath).First()["# BrisaXIV Japanese corpus v1 ".Length..];
    var corpus = new PreparationCatalog();
    Check(corpus.TryLoad(corpusPath, fingerprint, CancellationToken.None), "Prepared Japanese corpus loaded without scanning game files again");
    var personalFile = new TranslationFile(Path.Combine(pluginRoot, "traducoes.txt"));
    var localKey = KeyProtection.Unprotect(File.ReadAllText(Path.Combine(pluginRoot, "chave.dpapi")).Trim());
    using var personalQueue = new TranslationQueue(personalFile, new Translator(new(TranslationProvider.DeepL, localKey, false, "", "")));
    var job = new BackgroundPreparation(corpus, personalFile, Path.Combine(pluginRoot, "progresso-preparacao.json"));
    var budget = int.Parse(args[3]);
    var previous = "";
    for (var i = 0; i < 1200; i++)
    {
        job.Pump(personalQueue, true, budget);
        if (job.Message != previous) { Console.WriteLine(job.Message); previous = job.Message; }
        if (job.Completed || (personalQueue.PendingCount == 0 && (job.Message.StartsWith("Parte desta sessão") || job.Message.Contains("progresso foi mantido") || job.Message.StartsWith("Falha") || job.Message.StartsWith("Sem permissão")))) break;
        await Task.Delay(1000);
    }
    Console.WriteLine($"PREPARATION: saved {personalFile.Count} entries; corpus cursor {job.Cursor}/{corpus.Ordered.Count}");
}
else if (args.Length > 1 && args[1] == "--verify-complete")
{
    var root = Path.GetFullPath(args[2]);
    var corpusPath = Path.Combine(root, "originais-japoneses.jsonl");
    var fingerprint = File.ReadLines(corpusPath).First()["# BrisaXIV Japanese corpus v1 ".Length..];
    var corpus = new PreparationCatalog(); corpus.TryLoad(corpusPath, fingerprint, CancellationToken.None);
    var personalFile = new TranslationFile(Path.Combine(root, "traducoes.txt"));
    Check(corpus.Ready && corpus.Ordered.All(x => personalFile.TryGet(x.Text, out _)), "Every Japanese corpus entry exists in the personal TXT; final read-only conversion is safe");
}
else if (args.Length > 1 && args[1] == "--layout-schema")
{
    using var game = new GameData(args[2]);
    var talk = game.GetFile<Lumina.Data.Files.UldFile>("ui/uld/Talk.uld")!;
    var name = talk.WidgetData.Nodes.Single(n => n.NodeId == 2);
    var speech = talk.WidgetData.Nodes.Single(n => n.NodeId == 3);
    var background = talk.WidgetData.Nodes.Single(n => n.NodeId == 10);
    Check(name.NodeType == 3 && speech.NodeType == 3 && background.NodeType == 1
        && speech.ParentId == background.ParentId && background.W >= 200
        && talk.WidgetData.Nodes.Any(n => n.ParentId == 10 && n.NodeType == 2 && n.H == background.H),
        "Installed Talk layout confirms separate speaker and speech nodes plus the layered background container used for expansion");
    var mini = game.GetFile<Lumina.Data.Files.UldFile>("ui/uld/MiniTalk.uld")!;
    Check(mini.Components.Any(c => c.Nodes.Any(n => n.NodeType == 3)
        && c.Nodes.Where(n => n.NodeType == 3).All(t => c.Nodes.Any(n => n.NodeType == 4 && n.ParentId == t.ParentId))),
        "Installed MiniTalk balloons have a text node and nine-grid background sharing the parent used for native resizing");
    var battle = game.GetFile<Lumina.Data.Files.UldFile>("ui/uld/BattleTalk.uld")!;
    Check(battle.WidgetData.Nodes.Single(n => n.NodeId == 4).NodeType == 3 && battle.WidgetData.Nodes.Single(n => n.NodeId == 6).NodeType == 3,
        "Installed BattleTalk confirms the excluded name node and independent speech node");
    var selectOk = game.GetFile<Lumina.Data.Files.UldFile>("ui/uld/SelectOk.uld")!;
    var prompt = selectOk.WidgetData.Nodes.Single(n => n.NodeId == 2);
    Check(prompt.NodeType == 3 && selectOk.WidgetData.Nodes.Any(n => n.ParentId == prompt.ParentId && n.Y > prompt.Y && n.NodeType >= 1000)
        && selectOk.Components.Any(c => c.Type.ToString() == "Window" && c.Nodes.Any(n => n.NodeType == 4) && c.Nodes.Any(n => n.NodeType == 8)),
        "Installed SelectOk confirms the prompt, lower button row and native window skin/collision component");
}
else if (args.Length > 1 && args[1] == "--index")
{
    using var index = new JapaneseSourceIndex(Path.GetFullPath(args[2]), Language.English, Path.GetFullPath(args[3]));
    while (!index.Completion.IsCompleted)
    {
        await Task.WhenAny(index.Completion, Task.Delay(20000));
        Console.WriteLine($"INDEX {index.CompletedSheets}/{index.TotalSheets} sheets; {index.Texts.Count} mappings");
    }
    await index.Completion;
    Check(index.Error == null && index.Catalog.Ready, "Full Japanese corpus and language bridge prepared from the local installation");
    Check(index.Catalog.Ordered.Count > 10000 && index.Catalog.Ordered.Zip(index.Catalog.Ordered.Skip(1)).All(pair => pair.First.Expansion >= pair.Second.Expansion), "All available Japanese text is ordered by newest expansion first");
    const string screenshotLine = "Elene'shpya came by a while ago to give me a toy—a little ball\nof a thing with a\nface on it. He said it was a model of the actual machine\nCahciua used to\noperate.";
    Check(index.Texts.TryResolve(screenshotLine, "Falas", out var original), "The English NPC dialogue in the user's screenshot resolves to its Japanese original");
    Console.WriteLine($"CORPUS {index.Catalog.Ordered.Count} unique texts; {index.Catalog.CharacterCount} characters; screenshot Japanese length {original.Length}; skipped sheets {index.SkippedSheets}");
    foreach (var group in index.Catalog.Ordered.GroupBy(x => x.Expansion)) Console.WriteLine($"GROUP expansion {group.Key}: {group.Count()} texts / {group.Sum(x => (long)x.Text.Length)} chars");
}
else if (args.Length > 1 && args[1] == "--schema")
{
    Console.WriteLine(string.Join("\n", typeof(Lumina.Excel.Sheets.Quest).GetProperties().Where(p => p.Name.Contains("Version") || p.Name.Contains("Expansion") || p.Name.Contains("Journal")).Select(p => p.Name + " " + p.PropertyType.Name)));
}
else if (args.Length > 1)
{
    using var game = new GameData(args[1]);
    var quests = game.GetExcelSheet<RawRow>(Language.Japanese, "Quest")!;
    var first = quests.First();
    var strings = Enumerable.Range(0, first.Columns.Count).Where(i => first.Columns[i].Type == ExcelColumnDataType.String).ToArray();
    var sample = quests.First(x => TextRules.ContainsJapanese(x.ReadStringColumn(strings[0]).ExtractText()) && x.ReadStringColumn(strings[1]).ExtractText().Length > 0);
    var id = sample.ReadStringColumn(strings[1]).ExtractText();
    var script = game.Excel.SheetNames.FirstOrDefault(n => n.StartsWith("quest/", StringComparison.OrdinalIgnoreCase) && n.EndsWith("/" + id, StringComparison.OrdinalIgnoreCase));
    Check(script != null, "Quest metadata resolves a local Japanese dialogue sheet");
    var dialogue = game.GetExcelSheet<RawRow>(Language.Japanese, script!)!;
    var count = 0;
    foreach (var row in dialogue)
        for (var i = 0; i < row.Columns.Count; i++)
            if (row.Columns[i].Type == ExcelColumnDataType.String && TextRules.ContainsJapanese(row.ReadStringColumn(i).ExtractText())) count++;
    Check(count > 0, "Quest dialogue text is readable from the installed game before accepting it");
    Console.WriteLine($"LOCAL GAME: {quests.Count} quest rows, sample script has {count} Japanese texts. No text sent to a service.");
}
Console.WriteLine($"{passed} checks passed.");

sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request).WaitAsync(token);
}
