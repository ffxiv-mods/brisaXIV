# Brisa XIV

Plugin do Dalamud (API 15) que mostra o Final Fantasy XIV em **português do Brasil**, mesmo com o jogo em inglês.
Ele busca o original **japonês** de cada texto nos arquivos do próprio jogo e traduz a partir dele, o que costuma dar
um resultado mais fiel que traduzir do inglês. As traduções ficam num TXT permanente: um texto traduzido uma vez
nunca é enviado de novo a um tradutor.

## Por que traduzir do japonês

Nós, que desenvolvemos o Brisa XIV, queríamos viver a história do jogo do jeito que ela foi escrita originalmente.
A localização para o inglês muda o sentido de muita coisa, e muitas vezes sem necessidade, só para adaptar o texto
ao público norte-americano. Esse público não está tão próximo de nós, brasileiros, que crescemos vendo anime e já
estamos acostumados com a tradução que respeita o original.

Traduzir para o português a partir do inglês seria traduzir uma adaptação, perdendo boa parte do contexto e da
intenção do texto japonês. Por isso o Brisa XIV parte sempre do original japonês: acreditamos que assim a
experiência fica mais fiel e mais próxima do que os autores quiseram contar.

## Instalação

1. No jogo, abra `/xlsettings` → **Experimental** → **Custom Plugin Repositories**.
2. Adicione `https://github.com/ffxiv-mods/brisaXIV/releases/latest/download/repo.json`, marque a caixa e clique em **Save and Close**.
3. Abra `/xlplugins`, procure **Brisa XIV** e instale.

O pacote já vem com o catálogo de textos do jogo traduzido. Novas versões acrescentam ao seu TXT as traduções
publicadas que você ainda não tem, sem apagar as suas correções.

## O que é traduzido

Ligado por padrão, na aba **O que traduzir**:

- **Falas dos personagens**: a caixa de conversa com NPCs, balões e falas de batalha. Quando a tradução já existe, o
  próprio jogo monta a caixa e "digita" a fala em português. Falas longas usam uma fonte um pouco menor e, se
  precisar, a caixa cresce.
- **Legendas de cenas.**
- **Missões e objetivos** na HUD.
- **Diário de missões**: a lista e os detalhes da missão selecionada.

Na aba **Experimental**, desligado por padrão, ficam os textos que funcionam mas cujo encaixe no layout do jogo
ainda está sendo ajustado: descrições de itens e habilidades, menus e listas, confirmações Sim/Não e OK, avisos,
outras janelas, nomes de NPCs, nomes sobre a cabeça, locais e viagens, receitas e coleções.

## Como as traduções são feitas

1. **TXT local:** se o texto já foi traduzido, aparece na hora, sem internet.
2. **Original japonês:** o plugin encontra o original japonês do texto em inglês exibido, inclusive com números,
   nomes de itens e trechos que dependem do nível, montados como o jogo mostra.
3. **O que ainda falta** é traduzido na hora pelo **Google Tradutor** (rápido e sem chave). Se ele falhar, o texto
   passa para a **OpenAI** e depois para o **DeepL**, se você tiver configurado as chaves deles.
4. **Pré-tradução:** as missões aceitas (resumos do diário, objetivos e legendas) são traduzidas em segundo plano
   antes de você abri-las. Com as categorias experimentais ligadas, o mesmo vale para as habilidades do seu job e os
   itens do inventário e das hotbars.

Tudo o que é traduzido durante o jogo também fica separado em `traducoes-novas.txt`, no mesmo formato do TXT.

## Comandos

| Comando | O que faz |
| --- | --- |
| `/brisa` | Abre o painel de configuração |
| `/brisa ler` | Janela de leitura ampliada, com histórico das falas e o original abaixo da tradução |
| `/brisa progresso` | Progresso da preparação e da fila de traduções |
| `/brisa custos` | Custos estimados da IA (quando uma IA está configurada) |
| `/brisa jornal` | Liga ou desliga a tradução do diário de missões |
| `/brisa recarregar` | Relê o TXT depois de uma correção manual |
| `/brisa tudo` | Liga todas as categorias, inclusive as experimentais |
| `/brisa diagnostico` | Liga ou desliga o registro de diagnóstico (diário, falas, habilidades e itens), para relatar problemas |

## Compatibilidade

- Não use junto outro plugin que também traduza as mesmas janelas: os dois sobrescrevem o texto um do outro.
- Plugins que acrescentam linhas aos tooltips, como os de preço do mercado, continuam funcionando.
- O chat de jogadores, os campos de digitação e os nomes de jogadores nunca são lidos nem enviados.

## Privacidade e custos

Cada usuário usa as próprias chaves, guardadas protegidas pelo Windows. Para o Google, OpenAI ou DeepL vão apenas
textos do jogo que aparecem na tela e ainda não estão no TXT, além das missões aceitas na pré-tradução. Chaves,
históricos de custo, progresso, pendências, logs e índices locais ficam só no seu computador. Com a OpenAI, o painel
mostra **estimativas** de custo calculadas com os tokens informados pelo provedor.

## Documentação

- [Instalação e uso](LEIA-ME.md)
- [Publicação de versões](PUBLICAR.md)
- [Compilação e estrutura do código](DESENVOLVIMENTO.md)
- [Validação e limitações dos testes](VALIDACAO.md)

As imagens em `docs` são renderizações da interface com **dados fictícios**.
