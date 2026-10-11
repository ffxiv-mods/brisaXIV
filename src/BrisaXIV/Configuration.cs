using BrisaXIV.Core;
using Dalamud.Configuration;

namespace BrisaXIV;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 7;
    public bool Enabled = true;
    public bool NativeHud = true;
    public bool Dialogue = true;
    public bool Subtitles = true;
    public bool WindowTitles = true;
    public bool CharacterNames = false;
    public bool NamePlates = false;
    public bool? Locations;
    public bool? Crafting;
    public bool? Collections;
    public Dictionary<string, bool> CategoryTitles = new();
    public Dictionary<string, bool> CategoryDescriptions = new();
    // Window frame titles inherit "Títulos de janelas"; a content heading (the quest or item name in
    // a details window) is part of its category and inherits it, unless its title choice says otherwise.
    public bool TextPartEnabled(string category, bool title, bool windowFrame = true) => TextCategoryPolicy.PartEnabled(
        title ? CategoryTitles : CategoryDescriptions, category, !title || !windowFrame || WindowTitles);
    public bool Quests = true;
    public bool? Journal;
    public bool Items = false;
    public bool Actions = false;
    public bool Confirmations = false;
    public bool Menus = false;
    public bool OtherWindows = false;
    public bool JapaneseOriginals = true;
    public bool Notices = false;
    public bool TranslateUnmatched = true;
    public bool GoogleFallback = true;
    public bool GoogleForMissing = true;
    public bool Prefetch = true;
    public bool BackgroundPreparation = true;
    public bool CachedOnly = true;
    public bool PrioritizeDialogue = true;
    public bool WaitForDialogue = true;
    public int DialogueWaitSeconds = 10;
    public bool ContinuousPreparation = false;
    public int SessionCharacterBudget = 30000;
    public bool ShowJapanese = false;
    public float ReadingScale = 1.35f;
    public TranslationProvider Provider = TranslationProvider.DeepL;
    public string EncryptedApiKey = "";
    public string EncryptedOpenAIApiKey = "";
    public string OpenAIModel = "gpt-6-luna";
    public int OpenAIRequestsPerMinute = 5000;
    public int OpenAITokensPerMinute = 2_000_000;
    public int ParallelBatches = 4;
    public string EncryptedBillingAdminKey = "";
    public string BillingProfileId = "";
    public string BillingProjectId = "";
    public string BillingApiKeyId = "";
    public bool AutoRefreshBilling = false;
    public List<ModelTokenPrice> AIPrices = [AIPricing.Luna];
    public bool DeepLPro = false;
    public string OllamaUrl = "http://127.0.0.1:11434";
    public string OllamaModel = "gemma3:4b";
}
