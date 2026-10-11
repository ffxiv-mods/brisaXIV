using BrisaXIV.Core;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Data.Structs.Excel;

namespace BrisaXIV;

internal sealed record QuestEntry(uint RowId, string JapaneseName, string EnglishName, string ScriptId);

internal sealed class QuestCatalog(IDataManager data)
{
    private List<QuestEntry>? quests;

    public IReadOnlyList<QuestEntry> Search(string query)
    {
        quests ??= Load();
        query = query.Trim();
        if (query.Length < 2) return [];
        return quests.Where(q => q.JapaneseName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || q.EnglishName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || q.ScriptId.Contains(query, StringComparison.OrdinalIgnoreCase)
            || q.RowId.ToString() == query).Take(30).ToArray();
    }

    private List<QuestEntry> Load()
    {
        var ja = data.GetExcelSheet<RawRow>(ClientLanguage.Japanese, "Quest");
        var en = data.GetExcelSheet<RawRow>(ClientLanguage.English, "Quest");
        var first = ja.First();
        var columns = Enumerable.Range(0, first.Columns.Count).Where(i => first.Columns[i].Type == ExcelColumnDataType.String).ToArray();
        if (columns.Length < 2) throw new InvalidOperationException("Quest text columns not found");
        var output = new List<QuestEntry>();
        foreach (var row in ja)
        {
            var name = row.ReadStringColumn(columns[0]).ExtractText();
            var script = row.ReadStringColumn(columns[1]).ExtractText();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(script)) continue;
            var english = en.TryGetRow(row.RowId, out var englishRow) ? englishRow.ReadStringColumn(columns[0]).ExtractText() : "";
            output.Add(new(row.RowId, name, english, script));
        }
        return output;
    }

    public string[] GetLines(QuestEntry quest)
    {
        var names = data.GameData.Excel.SheetNames.Where(n => n.StartsWith("quest/", StringComparison.OrdinalIgnoreCase)
            && n.EndsWith("/" + quest.ScriptId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (names.Length == 0)
            throw new InvalidOperationException("Não foi localizada a tabela de falas dessa missão na instalação atual.");
        var texts = new HashSet<string>(StringComparer.Ordinal) { TextRules.Normalize(quest.JapaneseName) };
        foreach (var name in names)
        {
            var sheet = data.GetExcelSheet<RawRow>(ClientLanguage.Japanese, name);
            foreach (var row in sheet)
            {
                for (var i = 0; i < row.Columns.Count; i++)
                {
                    if (row.Columns[i].Type != ExcelColumnDataType.String) continue;
                    var text = TextRules.Normalize(row.ReadStringColumn(i).ExtractText());
                    if (TextRules.ContainsJapanese(text)) texts.Add(text);
                }
            }
        }
        return texts.ToArray();
    }
}
