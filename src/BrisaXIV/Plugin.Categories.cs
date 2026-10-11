using Dalamud.Bindings.ImGui;

namespace BrisaXIV;

public sealed partial class Plugin
{
    private void SetAllTextCategories(bool enabled)
    {
        config.Dialogue = config.Subtitles = config.WindowTitles = config.Quests = config.Items = config.Actions
            = config.Menus = config.Confirmations = config.Notices = config.OtherWindows = enabled;
        config.CharacterNames = config.NamePlates = enabled;
        config.Journal = enabled;
        config.Locations = config.Crafting = config.Collections = enabled;
        // Title/description choices inherit their main category again. Writing them here (as before)
        // kept a category silent after "Desativar todas" even when its main toggle was turned back on.
        config.CategoryTitles.Clear();
        config.CategoryDescriptions.Clear();
        PluginInterface.SavePluginConfig(config);
    }

    private static readonly string[] PartCategories = ["Missões", "Itens", "Habilidades", "Menus", "Confirmações", "Outras janelas", "Locais e viagens", "Receitas e fabricação", "Conquistas e coleções"];

    // The main toggle that a title/description subdivision belongs to.
    private bool MainCategoryEnabled(string part) => part switch
    {
        "Missões" => config.Quests || (config.Journal ?? config.Quests),
        "Itens" => config.Items,
        "Habilidades" => config.Actions,
        "Menus" => config.Menus,
        "Confirmações" => config.Confirmations,
        "Outras janelas" => config.OtherWindows,
        "Locais e viagens" => config.Locations ?? config.Menus,
        "Receitas e fabricação" => config.Crafting ?? config.Menus,
        "Conquistas e coleções" => config.Collections ?? config.Menus,
        _ => false,
    };

    // Turning a main category on brings back its title and description, whatever they were set to.
    private bool ClearPartsOfEnabledCategories(IReadOnlyDictionary<string, bool> before)
    {
        var cleared = false;
        foreach (var part in PartCategories.Where(part => !before[part] && MainCategoryEnabled(part)))
            cleared |= config.CategoryTitles.Remove(part) | config.CategoryDescriptions.Remove(part);
        return cleared;
    }

    // Repairs settings written by the old "Desativar todas": a category that is on with both its title
    // and description off can only come from it (turning the whole category off is the direct way).
    private void RepairSilencedCategories()
    {
        foreach (var part in PartCategories)
            if (MainCategoryEnabled(part) && config.CategoryTitles.TryGetValue(part, out var title) && !title
                && config.CategoryDescriptions.TryGetValue(part, out var description) && !description)
            { config.CategoryTitles.Remove(part); config.CategoryDescriptions.Remove(part); }
    }

    private void DrawTextCategories()
    {
        ImGui.TextWrapped("As opções são salvas automaticamente. Desligar uma categoria devolve seus textos ao idioma do jogo, sem apagar traduções do TXT ou alterar a preparação do catálogo.");
        if (ImGui.Button("Ativar todas")) SetAllTextCategories(true);
        ImGui.SameLine();
        if (ImGui.Button("Desativar todas")) SetAllTextCategories(false);
        var changed = false;
        var enabledBefore = PartCategories.ToDictionary(part => part, MainCategoryEnabled);
        BeginCard("StoryCategories"); ImGui.TextUnformatted("História e missões");
        changed |= ToggleRow("Falas dos personagens", ref config.Dialogue,
            "Traduz o conteúdo das conversas, os balões sobre personagens, falas de batalha e escolhas durante cenas. Os nomes dos NPCs continuam no idioma original.");
        changed |= ToggleRow("Legendas de cenas", ref config.Subtitles,
            "Traduz a janela de legendas TalkSubtitle nas cenas em que o jogo usa essa superfície. Funciona independentemente das caixas de conversa.");
        changed |= ToggleRow("Missões e objetivos", ref config.Quests,
            "Traduz aceitação, conclusão, objetivos e acompanhamento de missões na HUD. O diário e seus detalhes têm uma opção própria logo abaixo; inicialmente herdam esta escolha.");
        var journal = config.Journal ?? config.Quests;
        if (ToggleRow("Traduzir diário de missões", ref journal,
            "Liga ou desliga a tradução das duas janelas do Journal: lista de missões e detalhes da missão selecionada. Ao desligar, o texto original volta a aparecer sem apagar o TXT. Título e descrição seguem a subdivisão de Missões. Também pode alternar com /brisa jornal."))
        { config.Journal = journal; changed = true; }
        EndCard();
        BeginCard("DetailCategories"); ImGui.TextUnformatted("Detalhes e descrições");
        changed |= ToggleRow("Itens e descrições", ref config.Items,
            "Traduz a janela de detalhes exibida ao passar o cursor sobre um item, incluindo seu nome e descrição quando disponíveis. As listas do inventário pertencem à categoria Menus.");
        changed |= ToggleRow("Habilidades e ações", ref config.Actions,
            "Traduz a janela de detalhes de habilidades e ações ao passar o cursor. Pode ser ativada separadamente da tradução dos itens.");
        EndCard();
        BeginCard("InterfaceCategories"); ImGui.TextUnformatted("Interface do jogo");
        changed |= ToggleRow("Títulos de janelas", ref config.WindowTitles,
            "Padrão para títulos e subtítulos identificados nas janelas habilitadas. As escolhas individuais na subdivisão abaixo têm prioridade. Nomes de NPCs permanecem originais.");
        changed |= ToggleRow("Menus e listas", ref config.Menus,
            "Traduz comandos, configurações, inventários, lojas, listas e opções de conversa. As caixas Sim/Não e OK têm uma opção separada.");
        changed |= ToggleRow("Confirmações Sim/Não e OK", ref config.Confirmations,
            "Traduz perguntas e botões das caixas de confirmação SelectYesno e SelectOk, incluindo compras e mensagens de confirmação. As caixas compatíveis crescem para acomodar o texto.");
        changed |= ToggleRow("Avisos e mensagens do jogo", ref config.Notices,
            "Traduz avisos centrais, mensagens de erro, nomes de área anunciados, mudanças de classe e dicas apresentados nas superfícies de avisos compatíveis.");
        changed |= ToggleRow("Outras janelas e HUD", ref config.OtherWindows,
            "Inclui janelas compatíveis que ainda não têm uma categoria específica. Esta cobertura é experimental; a disponibilidade depende do tipo de campo e do layout do jogo.");
        EndCard();
        BeginCard("NamesAndCollections"); ImGui.TextUnformatted("Nomes, locais e atividades");
        changed |= ToggleRow("Nomes de personagens (NPCs)", ref config.CharacterNames,
            "Traduz nomes de NPCs e inimigos nos campos identificados de conversa e nas janelas habilitadas, como alvo e lista de inimigos. Desligado inicialmente para preservar nomes. Não altera nomes de jogadores, chat ou o nome do seu chocobo.");
        changed |= ToggleRow("Nomes sobre a cabeça (placas)", ref config.NamePlates,
            "Traduz os nomes flutuantes de NPCs, inimigos, mascotes e objetos do cenário. Jogadores, retentores e o seu chocobo permanecem com o nome original.");
        var locations = config.Locations ?? config.Menus;
        if (ToggleRow("Locais e viagens", ref locations,
            "Controla os anúncios de área (_AreaText) e a janela de teleporte (Teleport), incluindo destinos e informações. Inicialmente cada tela segue sua opção anterior de avisos ou menus; ao escolher aqui, ambas passam a seguir esta opção."))
        { config.Locations = locations; changed = true; }
        var crafting = config.Crafting ?? config.Menus;
        if (ToggleRow("Receitas e fabricação", ref crafting,
            "Controla RecipeNote e RecipeTree: lista de receitas, materiais e informações de fabricação. Detalhes de um item ao passar o cursor continuam seguindo Itens."))
        { config.Crafting = crafting; changed = true; }
        var collections = config.Collections ?? config.Menus;
        if (ToggleRow("Conquistas e coleções", ref collections,
            "Controla conquistas, montarias, mascotes, guia de pesca e diário de exploração nas janelas compatíveis. Detalhes de itens continuam seguindo Itens."))
        { config.Collections = collections; changed = true; }
        EndCard();
        changed |= ClearPartsOfEnabledCategories(enabledBefore);
        if (changed) PluginInterface.SavePluginConfig(config);
        BeginCard("CategoryParts");
        ImGui.TextUnformatted("Título e descrição por categoria");
        ImGui.TextWrapped("A categoria principal precisa estar ligada. Título controla cabeçalhos identificados; descrição controla os demais textos da tela, incluindo rótulos e botões. Campos sem identificação segura permanecem em descrição.");
        foreach (var category in PartCategories)
        {
            ImGui.PushID(category);
            if (ImGui.CollapsingHeader(category))
            {
                var title = config.TextPartEnabled(category, true);
                var description = config.TextPartEnabled(category, false);
                var titleHelp = category switch
                {
                    "Itens" => "Nome do item na janela ItemDetail e seus cabeçalhos padrão. Não altera nomes em listas do inventário.",
                    "Missões" => "Nome da missão nas telas JournalDetail e JournalAccept e cabeçalhos padrão. Outras listas e telas sem campo identificado seguem descrição.",
                    _ => "Títulos e subtítulos de componentes padrão de janela. Nomes específicos sem identificação segura seguem descrição."
                };
                if (ToggleRow("Título", ref title, titleHelp))
                { config.CategoryTitles[category] = title; PluginInterface.SavePluginConfig(config); }
                if (ToggleRow("Descrição e demais textos", ref description,
                    "Conteúdo da tela fora dos títulos identificados: descrições, objetivos, informações, rótulos, opções e botões. Não apaga traduções do TXT."))
                { config.CategoryDescriptions[category] = description; PluginInterface.SavePluginConfig(config); }
            }
            ImGui.PopID();
        }
        EndCard();
        ImGui.TextWrapped("Chat de jogadores e campos de entrada permanecem preservados. Nomes de NPCs e placas flutuantes seguem suas opções específicas. Textos já salvos usam o TXT; escolher categorias não reenvia suas traduções para a API.");
    }
}
