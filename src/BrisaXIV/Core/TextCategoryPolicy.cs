namespace BrisaXIV.Core;

public static class TextCategoryPolicy
{
    public static bool IsJournal(string addon) => addon is "Journal" or "JournalDetail";
    public static string? SpecializedCategory(string addon) => addon switch
    {
        "_AreaText" or "Teleport" => "Locais e viagens",
        "RecipeNote" or "RecipeTree" => "Receitas e fabricação",
        "Achievement" or "MountNote" or "MinionNote" or "FishGuide" or "AdventureNoteBook" => "Conquistas e coleções",
        _ => null
    };
    public static bool PartEnabled(IReadOnlyDictionary<string, bool>? choices, string category, bool inherited)
        => choices != null && choices.TryGetValue(category, out var enabled) ? enabled : inherited;
    public static string PartCategory(string addon, string category) => addon switch
    {
        "ItemDetail" => "Itens", "ActionDetail" => "Habilidades",
        "SelectYesno" or "SelectOk" => "Confirmações", _ => SpecializedCategory(addon) ?? category
    };
    public static bool IsSubtitle(string addon) => addon == "TalkSubtitle";
    public static bool IsConfirmation(string addon) => addon is "SelectYesno" or "SelectOk";
    // Standard Window components expose title and subtitle in nodes 3 and 4.
    public static bool IsWindowHeading(bool windowComponent, uint nodeId) => windowComponent && nodeId is 3 or 4;
    public static bool IsNodeEnabled(bool categoryEnabled, bool titlesEnabled, bool heading, bool speakerName)
        => categoryEnabled && !speakerName && (!heading || titlesEnabled);
}
