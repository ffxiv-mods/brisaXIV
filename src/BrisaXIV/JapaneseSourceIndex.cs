using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using BrisaXIV.Core;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Lumina.Excel.Exceptions;

namespace BrisaXIV;

// This worker owns its GameData instance and never reads live game pointers or calls a provider.
internal sealed class JapaneseSourceIndex : IDisposable
{
    public SourceTextIndex Texts { get; } = new();
    public PreparationCatalog Catalog { get; } = new();
    private readonly CancellationTokenSource stop = new();
    public Task Completion { get; }
    public int CompletedSheets;
    public int TotalSheets;
    public int SkippedSheets;
    public string? Error;
    public string FilePath { get; }
    public string GamePath { get; }
    public string CatalogPath { get; }
    private readonly Dictionary<string, int> scriptExpansions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, int> questExpansions = new();

    public JapaneseSourceIndex(string gamePath, Language displayLanguage, string outputDirectory)
    {
        GamePath = gamePath;
        FilePath = Path.Combine(outputDirectory, $"fontes-{displayLanguage}-JA.jsonl");
        CatalogPath = Path.Combine(outputDirectory, "originais-japoneses.jsonl");
        Completion = Task.Run(() => Build(displayLanguage));
    }

    public static string Scope(string sheet)
    {
        if (sheet.StartsWith("quest/", StringComparison.OrdinalIgnoreCase) || sheet.StartsWith("cut_scene/", StringComparison.OrdinalIgnoreCase)
            || sheet.Contains("Talk", StringComparison.OrdinalIgnoreCase)) return "Falas";
        if (sheet.StartsWith("Quest", StringComparison.OrdinalIgnoreCase) || sheet.StartsWith("Journal", StringComparison.OrdinalIgnoreCase)) return "Missões";
        if (sheet.StartsWith("Item", StringComparison.OrdinalIgnoreCase) || sheet.StartsWith("Action", StringComparison.OrdinalIgnoreCase)
            || sheet is "Status" or "Trait" or "Recipe") return "Itens e ações";
        if (sheet is "Addon" or "MainCommand" or "LogMessage" or "ContentFinderCondition") return "Menus";
        return "Outras janelas";
    }

    private void Build(Language language)
    {
        try
        {
            var versionFile = Path.Combine(Directory.GetParent(GamePath)!.FullName, "ffxivgame.ver");
            var version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GamePath + version)));
            var header = $"# BrisaXIV sources v4 {language} {fingerprint}";
            if (TryLoad(header) && Catalog.TryLoad(CatalogPath, fingerprint, stop.Token)) { Texts.Ready = true; return; }
            using var game = new GameData(GamePath, new LuminaOptions { DefaultExcelLanguage = language, LoadMultithreaded = false });
            LoadExpansions(game);
            var names = game.Excel.SheetNames.OrderByDescending(ExpansionForSheet).ThenBy(n => n, StringComparer.Ordinal).ToArray();
            TotalSheets = names.Length;
            foreach (var name in names)
            {
                stop.Token.ThrowIfCancellationRequested();
                try { AddSheet(game, name, language); }
                catch (UnsupportedLanguageException) { SkippedSheets++; }
                catch (NotSupportedException) { SkippedSheets++; }
                catch (FileNotFoundException) { SkippedSheets++; }
                catch (KeyNotFoundException) { SkippedSheets++; }
                CompletedSheets++;
            }
            Save(header);
            Catalog.Finish(fingerprint);
            Catalog.Save(CatalogPath, stop.Token);
            Texts.Ready = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = "Não foi possível preparar todos os originais japoneses (" + ex.GetType().Name + ")."; Texts.Ready = true; }
    }

    private void AddSheet(GameData game, string name, Language language)
    {
        var japaneseRaw = game.Excel.GetRawSheet(name, Language.Japanese);
        if (japaneseRaw.Language is not (Language.Japanese or Language.None)) return;
        RawExcelSheet? sourceRaw = null;
        try { sourceRaw = game.Excel.GetRawSheet(name, language); }
        catch (UnsupportedLanguageException) { }
        var columns = Enumerable.Range(0, japaneseRaw.Columns.Count).Where(i => japaneseRaw.Columns[i].Type == ExcelColumnDataType.String).ToArray();
        if (columns.Length == 0) return;
        var scope = Scope(name);
        var expansion = ExpansionForSheet(name);
        if (japaneseRaw is RawSubrowExcelSheet)
        {
            var source = sourceRaw?.Language == language ? game.GetSubrowExcelSheet<RawSubrow>(language, name) : null;
            var japanese = game.GetSubrowExcelSheet<RawSubrow>(Language.Japanese, name)!;
            foreach (var group in japanese)
            foreach (var row in group)
            {
                stop.Token.ThrowIfCancellationRequested();
                foreach (var column in columns)
                {
                    var ja = row.ReadStringColumn(column).ExtractText();
                    Catalog.Add(ja, expansion, name);
                    if (source != null && source.TryGetSubrow(row.RowId, row.SubrowId, out var display)
                        && column < sourceRaw!.Columns.Count && sourceRaw.Columns[column].Type == ExcelColumnDataType.String)
                        Texts.Add(display.ReadStringColumn(column).ExtractText(), ja, scope);
                }
            }
        }
        else
        {
            var source = sourceRaw?.Language == language ? game.GetExcelSheet<RawRow>(language, name) : null;
            var japanese = game.GetExcelSheet<RawRow>(Language.Japanese, name)!;
            var names = IsNameSheet(name);
            foreach (var row in japanese)
            {
                stop.Token.ThrowIfCancellationRequested();
                var rowExpansion = name == "Quest" && questExpansions.TryGetValue(row.RowId, out var version) ? version : expansion;
                foreach (var column in columns)
                {
                    var ja = row.ReadStringColumn(column).ExtractText();
                    Catalog.Add(ja, rowExpansion, name);
                    if (source != null && source.TryGetRow(row.RowId, out var display)
                        && column < sourceRaw!.Columns.Count && sourceRaw.Columns[column].Type == ExcelColumnDataType.String)
                    {
                        var shown = display.ReadStringColumn(column).ExtractText();
                        Texts.Add(shown, ja, scope);
                        if (names && column == columns[0]) Texts.AddName(shown, ja);
                    }
                }
            }
        }
    }

    // The first string column of these sheets is the singular name shown in the HUD.
    private static bool IsNameSheet(string sheet) => sheet is "BNpcName" or "ENpcResident" or "EObjName";

    private void LoadExpansions(GameData game)
    {
        var typed = game.GetExcelSheet<Lumina.Excel.Sheets.Quest>(Language.Japanese)!;
        var raw = game.GetExcelSheet<RawRow>(Language.Japanese, "Quest")!;
        var first = raw.First();
        var strings = Enumerable.Range(0, first.Columns.Count).Where(i => first.Columns[i].Type == ExcelColumnDataType.String).ToArray();
        foreach (var quest in typed)
        {
            var expansion = (int)quest.Expansion.RowId;
            questExpansions[quest.RowId] = expansion;
            if (strings.Length < 2 || !raw.TryGetRow(quest.RowId, out var row)) continue;
            var script = row.ReadStringColumn(strings[1]).ExtractText();
            if (script.Length > 0) scriptExpansions[script] = expansion;
        }
    }

    private int ExpansionForSheet(string sheet)
    {
        var slash = sheet.LastIndexOf('/');
        return slash >= 0 && scriptExpansions.TryGetValue(sheet[(slash + 1)..], out var expansion) ? expansion : -1;
    }

    private bool TryLoad(string header)
    {
        if (!File.Exists(FilePath)) return false;
        using var reader = File.OpenText(FilePath);
        if (reader.ReadLine() != header) return false;
        while (reader.ReadLine() is { } line)
        {
            stop.Token.ThrowIfCancellationRequested();
            var record = JsonSerializer.Deserialize<string?[]>(line);
            if (record is { Length: 2 } && record[0] is { } key) Texts.Import(key, record[1]);
        }
        return true;
    }

    private void Save(string header)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(header);
                var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                foreach (var record in Texts.Export()) { stop.Token.ThrowIfCancellationRequested(); writer.WriteLine(JsonSerializer.Serialize(record, options)); }
            }
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() { stop.Cancel(); }
}
