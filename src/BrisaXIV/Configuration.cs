using BrisaXIV.Core;
using Dalamud.Configuration;

namespace BrisaXIV;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;
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
    public bool TextPartEnabled(string category, bool title) => TextCategoryPolicy.PartEnabled(
        title ? CategoryTitles : CategoryDescriptions, category, title ? WindowTitles : true);
    public bool Quests = true;
    public bool? Journal;
    public bool Items = true;
    public bool Actions = true;
    public bool Confirmations = true;
    public bool Menus = true;
    public bool OtherWindows = true;
    public bool JapaneseOriginals = true;
    public bool Notices = true;
    public bool TranslateUnmatched = true;
    public bool GoogleFallback = true;
    public bool GoogleForMissing = true;
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
