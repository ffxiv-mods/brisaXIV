# Brisa XIV — japonês para português do Brasil

Projeto para Dalamud API 15, versão 0.8.4. As traduções iniciais podem ser compartilhadas em um pacote compactado; chaves, progresso, custos, logs e índices locais ficam fora do Git. A instalação local não exige publicação. Para testar pelo repositório experimental, siga [PUBLICAR.md](PUBLICAR.md).

## Nova interface e custos da IA

`/brisa` abre uma janela escura com navegação lateral e cartões, inspirada no exemplo Auto FATE Grind. **Progresso**, **Geral**, **Tradutor**, **HUD e textos**, **Preparação**, **Arquivo TXT**, **Missões** e **Teste de tradução** organizam as funções existentes. `/brisa progresso` abre o acompanhamento e `/brisa custos` abre custos quando uma IA está configurada; caso contrário, abre Tradutor.

**Custos da IA** aparece quando OpenAI ou Ollama está selecionado, configurado e aplicado. Mostra chamadas, entrada, cache e saída informados pelo provedor, totais por modelo e gráfico diário. É possível selecionar este mês, mês anterior ou últimos 30 dias. Os dias usam UTC; o horário da última consulta aparece na hora local. O arquivo privado `uso-ia.jsonl` mantém o histórico entre recargas.

A **estimativa do Brisa** calcula tokens × tarifas. Não usa a reserva conservadora do limitador TPM. Cache faz parte dos tokens de entrada e recebe preço próprio, sem contagem dupla. Respostas rejeitadas, truncadas ou recuperadas também podem consumir tokens e entram no histórico. Uma resposta sem contadores válidos fica desconhecida; modelos ou níveis sem tarifa ficam sem estimativa. O TXT antigo não permite reconstruir custos passados e não é enviado novamente para essa finalidade. Ollama não gera custo de API externa; seu histórico pode incluir chamadas OpenAI anteriores.

As tarifas padrão verificadas em 10/10/2026 para [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) são, por milhão de tokens: US$ 0,10 entrada, US$ 0,01 cache, US$ 0,50 saída e US$ 0,125 escrita de cache. O cálculo distingue níveis de serviço e contexto longo. Em **Tarifas usadas na estimativa**, você pode ajustar os preços; a tabela atual recalcula o histórico. Créditos, descontos, impostos e outros ajustes específicos da conta não são inferidos.

Para conferir custos informados pela OpenAI, expanda **Configurar consulta administrativa** dentro dessa página. Informe **no plugin** uma credencial administrativa separada, com permissão para consultar custos; a chave comum de tradução pode não dar acesso. Ela é protegida pelo Windows e não é compartilhada com o tradutor. Você pode filtrar por ID de projeto ou ID da chave — IDs, não segredos. Clique em **Salvar consulta de custos** e **Consultar agora**. A atualização a cada 15 minutos é opcional e funciona enquanto a página está aberta. Não informe essa chave em chats ou no Git.

O [endpoint de custos da OpenAI](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage/methods/costs) informa valores da conta/projeto/chave, que podem incluir outros aplicativos. O painel mostra o escopo e a hora da consulta. Um valor ausente ou uma consulta que falhou fica **Não verificado**. Um zero informado não confirma gratuidade nem consolidação da cobrança. Não afirmamos que houve débito no cartão. **Cobertura gratuita** permanece **Não informada** porque esses dados não permitem provar quanto foi coberto por créditos ou promoção. As barras da estimativa e da conta são separadas e não devem ser somadas.

Quando a OpenAI retornar `credit_balance_exhausted`, surge um **alerta de créditos esgotados**. Outros erros de gasto/cota mostram sua causa correspondente. As duas filas deixam de fazer novos envios; o TXT, as pendências e o progresso continuam disponíveis. Requisições já enviadas podem terminar. Fechar o alerta mantém a pausa. Depois de regularizar o saldo ou limite, use **Tentar novamente após erro** em Progresso ou Arquivo TXT. Limites temporários de RPM/TPM continuam com espera e nova tentativa automática, sem serem apresentados como falta de crédito. A distinção segue os [códigos oficiais de erro](https://developers.openai.com/api/docs/guides/error-codes).

## Repositório e dados pessoais

O TXT contém as traduções autorizadas pelo proprietário. A exportação usa `data/traducoes-iniciais.zip` ou partes compactadas, além do TXT dentro de `latest.zip`. Não copie para o Git `chave.dpapi`, configuração pessoal, `uso-ia.jsonl`, `custos-ia.json`, logs, `progresso-preparacao.json`, `pendencias-*.json`, cópias `.bak` ou índices locais. O `.gitignore` protege esses caminhos. Cada usuário informa suas próprias chaves no `/brisa`.

## Atualizar o plugin que você já habilitou

1. Para carregar esta atualização, use `/xlplugins`, desative **Brisa XIV** e ative novamente uma vez. As novas tentativas posteriores são automáticas na mesma sessão, sem reiniciar o jogo.
2. Você pode manter o jogo em inglês. O Brisa procura o original japonês nas tabelas da instalação.
3. Use `/brisa progresso` para acompanhar o trabalho. Em **HUD e textos**, as opções de falas, missões, itens, habilidades, menus, avisos e outras janelas vêm ativadas. `/brisa tudo` reativa essa cobertura.
4. Em **Preparação**, marque **Preparação contínua sem limite por abertura** para trabalhar enquanto o jogo estiver aberto, sem clicar repetidamente em Retomar. Com essa opção desmarcada, o padrão é até **30.000 caracteres novos por abertura**.
5. A HUD lê o TXT. Falas abertas e escolhas de NPCs recebem prioridade para traduzir e salvar o que ainda faltar. O painel `/brisa ler` oferece fonte ajustável e histórico de falas.

O caminho da DLL já registrado permanece `plugin\BrisaXIV.dll`. Se ainda não estiver registrada, com o jogo fechado execute `Instalar-No-Dalamud.ps1` e habilite Brisa XIV em **Dev Tools / Installed Dev Plugins** no `/xlplugins`.

Na instalação normal pelo repositório experimental, os arquivos pessoais ficam na pasta persistente de dados do Dalamud. O caminho exato aparece em **Arquivo TXT**. O pacote importa o TXT inicial uma vez e preserva traduções/progresso nas próximas atualizações. A instalação de desenvolvimento mantém os arquivos junto da DLL.

## Progresso e traduções simultâneas

`/brisa progresso` abre a nova aba **Progresso**. Ela mostra os textos salvos no TXT, os caracteres originais e em português, as palavras em português e a barra de confirmação do catálogo. Os totais do TXT são recalculados ao abrir/recarregar o arquivo e continuam disponíveis após reiniciar o jogo. Correções humanas substituem a contagem anterior; linhas repetidas não entram duas vezes.

A tabela de etapas separa **Na fila**, **Preparando / aguardando vaga**, **Esperando limite / nova tentativa**, **Enviados / esperando resposta**, **Validando / gravando** e **Pendentes — nova tentativa depois**. Use o filtro **Salvos** para ver as traduções recentes concluídas; **Erros / interrompidos** inclui pendências e **Prioridade da HUD** mostra falas abertas. Clique em um texto para ler seu original, tradução, grupo, idioma, serviço, quantidade de tentativas e quantos textos ainda devem ser salvos antes de tentar novamente.

Na subaba **Envios à API**, cada linha representa uma requisição iniciada. Clique nela para ver os textos exatos usados no envio, inclusive fragmentos de textos longos, o serviço, horário e duração. Os contadores da sessão mostram requisições, trechos, caracteres enviados, caracteres/palavras salvos e envios nos últimos 60 segundos. Novas tentativas contam novamente como envios, mas cada original salvo entra apenas uma vez nos totais permanentes. Iniciar uma requisição não prova que o servidor recebeu o texto; a indicação **Resposta recebida/validada** distingue essa etapa.

O histórico de envios é limitado e pertence à instância atual do plugin. Reaplicar o tradutor mantém os contadores; descarregar o plugin ou reiniciar o jogo começa outra sessão. Não é possível reconstruir o histórico de API das traduções antigas apenas pelo TXT. As palavras são unidades separadas por espaços contendo letras ou números. Para japonês, use o contador de caracteres: não há segmentação linguística japonesa. Caracteres são valores Unicode, incluindo espaços e quebras de linha; caracteres fora do plano básico contam uma vez.

O Luna agora processa **4 lotes simultâneos** por padrão, com **1 vaga adicional reservada para as falas da HUD**, compartilhadas entre as filas japonesa e do idioma do cliente. Em **Configurações → Tradutor → OpenAI**, ajuste **Lotes simultâneos em segundo plano** entre 1 e 32 e clique em **Salvar e aplicar tradutor**. São operações assíncronas no plugin, sem abrir vários processos. As gravações e a deduplicação permanecem sincronizadas.

O preparador alimenta uma janela maior de trabalho e a verifica a cada 250 ms no modo simultâneo, em vez de limitar a alimentação a 25 textos por segundo. Os limites 5.000 RPM e 2.000.000 TPM continuam compartilhados por todas as requisições, incluindo falas e novas tentativas. A aba mostra os limites ativos, os limites menores informados pelo servidor, esperas e reservas estimadas nos últimos 60 segundos. Reservas estimadas não são tokens faturados. Aumentar os lotes pode reduzir a espera entre chamadas; não garante atingir o teto, pois a latência, o tamanho dos lotes e os limites de tokens também controlam a velocidade. O tratamento segue a [documentação oficial de limites da OpenAI](https://developers.openai.com/api/docs/guides/rate-limits).

**Pausar preparação** deixa de adicionar novos textos; os lotes já na fila e pendências registradas podem terminar. **Preparação contínua** mantém o trabalho além do orçamento por abertura. A conferência compara todos os originais do catálogo com o TXT, incluindo posições anteriores ao cursor salvo e traduções prontas fora de ordem. Enquanto examina o arquivo, a barra usa a sequência salva; depois mostra todos os originais confirmados. **Conferir se falta alguma tradução** repete essa verificação sem chamar a API para textos já salvos. Recarregar um TXT editado também inicia uma nova conferência. A conclusão só ocorre com todos os originais presentes. A atualização preserva os formatos do TXT, índice e progresso.

## Falhas e novas tentativas automáticas

Uma tradução inválida, resposta vazia, marcador alterado, falha temporária ou problema de conexão vira uma pendência; os outros textos continuam. O original que falhou volta depois de **50 outros textos salvos**. Se falhar novamente, aguarda 100, 200, 400 e depois até 500. Lotes inválidos são tentados individualmente para isolar o problema. Resultados parciais já validados permanecem no TXT.

Se restarem apenas pendências, a fila tenta por intervalo: 30 segundos na primeira falha, 2 minutos na segunda, depois intervalos crescentes até 1 hora. Essa alternativa impede ficar parado no fim do catálogo quando não há mais textos para atingir a contagem. Esperas mínimas informadas pela API prevalecem sobre a contagem e o botão manual. Não é necessário reiniciar o jogo nem clicar em Retomar a cada falha.

As pendências ficam em `plugin\pendencias-ja.json` e `plugin\pendencias-cliente-IDIOMA.json`, separadas do TXT. Cada registro guarda o original, grupo, tentativas, erro resumido e quando poderá voltar; não contém chave da API. Ele só é removido após confirmar a tradução no TXT. Ao reaplicar o tradutor ou abrir o plugin novamente, entradas já salvas são descartadas da lista de pendências sem outra chamada. Falhas antigas que não tenham esse registro são recuperadas pela conferência do catálogo.

Chave inválida, falta de saldo, modelo indisponível e impossibilidade de gravar o TXT continuam pausando as solicitações, pois exigem corrigir a conta ou o arquivo. Depois da correção, **Tentar novamente após erro** retoma na mesma sessão. Nenhum erro ou texto com marcadores inválidos é gravado como tradução concluída.

## Testar a fala de um NPC agora

No `/brisa`, a opção **Priorizar falas e escolhas do NPC na HUD, mesmo em modo TXT** vem ativada. Converse com um NPC e mantenha a caixa aberta até chegar o português.

Se a fala já existe no arquivo, a HUD aplica a tradução antes de desenhar a caixa, sem a espera fixa de 350 ms. A correspondência japonesa precisa estar disponível no índice local. Se ainda faltar, o plugin consulta o original japonês e coloca a fala à frente do trabalho geral, mesmo com **HUD lê somente o TXT** marcada. Escolhas de conversa e caixas de aceitar ou concluir uma missão também podem entrar com prioridade. Somente textos novos aguardam aproximadamente 350 ms de estabilidade, para não traduzir cada letra da animação.

**Mostrar Traduzindo… na caixa enquanto aguarda uma fala nova** vem ativada. A caixa principal, os balões pequenos e as falas de batalha permanecem visíveis e mostram **Traduzindo…** no próprio campo de texto, por até **10 segundos**, sem alterar a transparência ou esconder a janela inteira. O nome do NPC continua visível. Quando a tradução chega, o português substitui o indicador automaticamente, sem outro clique. A espera máxima pode ser ajustada entre 1 e 30 segundos. Se houver erro ou a espera terminar, o original aparece e o português entra quando estiver disponível. Textos salvos não usam essa espera. A espera pertence a cada campo, para que dois balões simultâneos não interfiram um no outro.

Durante a animação de fechamento, o texto traduzido permanece aplicado. Ele só é restaurado ao desativar a tradução/HUD, excluir aquela categoria ou descarregar o plugin. Uma nova fala invalida o resultado anterior, evitando reaplicar uma tradução antiga ao próximo texto. Depois de atualizar uma versão que já deixou uma caixa invisível, feche a conversa atual e abra uma vez para limpar o estado antigo.

Os nomes no cabeçalho dos diálogos e das falas de batalha são preservados, assim como campos cujo texto corresponde ao nome de um NPC próximo. Isso também vale quando existe uma tradução antiga desse nome no TXT.

Durante a espera, `/brisa` mostra **Prioridade da HUD**. A preparação geral deixa de adicionar textos e retoma após as falas. No Google, ela cede a vez entre chamadas individuais. Uma chamada já enviada pode terminar primeiro; textos longos são concluídos e salvos antes de ceder a vez, para aproveitar os trechos já traduzidos. Uma linha que já estava enfileirada ganha prioridade sem criar outra tradução da mesma linha.

O limite de caracteres por abertura se aplica à preparação geral. As falas priorizadas podem consumir cota além dessa parte. O histórico de falas não dispara novas traduções; ele apenas lê resultados existentes ou em andamento.

Depois que o catálogo inteiro estiver concluído, a exceção de prioridade deixa de solicitar traduções. A DLL final somente de leitura também não tem essa função online.

A atualização preserva o TXT, o progresso e o catálogo. Recarregar interrompe a instância antiga do plugin; as traduções gravadas permanecem disponíveis. Uma requisição que já chegou ao serviço pode consumir cota mesmo se o plugin for recarregado antes de salvar a resposta. Evite recarregar repetidamente enquanto a fila estiver ocupada.

## Preparação completa em partes

A varredura desta instalação encontrou **533.724 textos japoneses distintos**, totalizando **19.127.667 caracteres**, em 7.912 tabelas examinadas. O catálogo abrange todos os campos contendo japonês que puderam ser lidos, incluindo falas, nomes de missões, descrições, itens, ações, menus e avisos. Textos repetidos entram apenas uma vez.

Missões e seus scripts são associados ao campo de expansão dos dados locais e preparados nesta ordem:

Dawntrail → Endwalker → Shadowbringers → Stormblood → Heavensward → A Realm Reborn.

Textos de outras tabelas sem associação explícita a uma expansão ficam depois desses grupos. Não se atribui uma expansão por adivinhação. Novos patches podem acrescentar tabelas ou textos: o catálogo reconhece a versão instalada, mantendo todas as traduções existentes.

A preparação usa uma fila fora da thread do jogo. Cada resultado é gravado assim que chega, inclusive quando uma chamada posterior do Google falha. O progresso só avança sobre entradas confirmadas no TXT. Ao fechar o jogo, requisições em andamento podem já ter sido cobradas pelo serviço; entradas concluídas permanecem salvas. A próxima abertura retoma do progresso confirmado e pula o que já existe.

O modo por partes limita cada sessão a 30.000 caracteres por padrão. **Preparação contínua** ignora esse limite e pode ser ativada mesmo depois de acabar a parte da sessão; a fila retoma sem apagar traduções. Continua respeitando a prioridade das falas, a ordem das expansões e a cota/disponibilidade dos serviços. Para pausar, desmarque **Retomar preparação automaticamente ao abrir o jogo**. A conclusão depende do tempo com o plugin aberto e da disponibilidade dos tradutores. A tradução automática pode precisar de correções de nomes e termos.

## DeepL e alternativa Google

A chave fornecida já está protegida com DPAPI em `plugin\chave.dpapi`, para o usuário atual do Windows. Ela não foi inserida no código nem na DLL. Se necessário, informe uma chave **DeepL API Free** no `/brisa` e use **Salvar e aplicar tradutor**; deixe API Pro desmarcada para a sua conta Free.

O padrão envia japonês ao DeepL Free, com destino PT-BR. Se a API responder **HTTP 456**, indicando limite de uso, o preparador passa automaticamente ao **Google Tradutor web**, sem chave adicional. Ele mantém essa alternativa por 24 horas nesta sessão; uma nova abertura ou **Tentar novamente** permite consultar o DeepL de novo. A chave DeepL nunca é enviada ao Google.

A alternativa Google usa endpoints web sem contrato de estabilidade; podem falhar, mudar ou limitar requisições. Primeiro é usado o endpoint da extensão de dicionário do Chrome (o mesmo do Echoglossian), que identifica a extensão com a chave pública dela e envia o texto na URL; se ele falhar ou bloquear, o mesmo texto vai ao `translate_a/single` (gtx), por POST. O ritmo é de uma requisição por segundo. Se todos os endpoints bloquearem (302/403/429), as filas aguardam 20 segundos, dobrando a cada bloqueio seguido até cinco minutos; o primeiro sucesso volta a 20 segundos. Os textos pendentes têm nova tentativa automática e o progresso permanece salvo. A chave DeepL ou OpenAI nunca é enviada ao Google. Ollama local continua opcional.

A preparação em segundo plano e as falas priorizadas solicitam traduções por padrão. A opção **HUD lê somente o TXT** mantém as demais janelas independentes da rede; desmarque também a prioridade de falas se quiser que toda a HUD apenas leia o arquivo. Se você desmarcar o modo TXT durante a preparação, outros textos novos na tela também poderão ser traduzidos. Ao concluir o catálogo, o modo de leitura é ativado e a prioridade não solicita novas traduções.

## OpenAI — GPT-6 Luna

1. Recarregue **Brisa XIV** em `/xlplugins` após esta atualização.
2. Abra `/brisa` e selecione **OpenAI — GPT-6 Luna** na lista **Tradutor**.
3. Informe sua **chave da API OpenAI** no campo de senha do plugin. Ela é diferente da chave DeepL e fica protegida por DPAPI na configuração do Dalamud. Evite enviar chaves pelo chat.
4. Mantenha o modelo **gpt-6-luna**, o limite **5.000 RPM** e **2.000.000 TPM**, ou informe limites menores se sua conta exigir.
5. Clique em **Salvar e aplicar tradutor**. Use **Traduzir texto de teste** com uma frase ainda ausente do TXT para verificar uma chamada real. Uma frase já salva é lida do arquivo sem chamar a API.
6. A preparação retoma usando o mesmo TXT e a mesma posição confirmada. Marque **Preparação contínua** para seguir enquanto o jogo estiver aberto.

O provedor usa a [Responses API](https://developers.openai.com/api/docs/guides/text) com o [modelo gpt-6-luna](https://developers.openai.com/api/docs/models/gpt-6-luna). A chave precisa ter acesso ao modelo e saldo disponível na API. Textos novos são enviados à OpenAI e o uso pode ser cobrado. Não são solicitadas ferramentas externas, navegação ou histórico de conversa; `store=false` desativa o armazenamento da resposta pela Responses API, sem equivaler a uma garantia de retenção zero de todos os dados do serviço.

Cada lote contém até 20 textos curtos e no máximo 8.000 caracteres, para reduzir a demora de falas prioritárias. A saída JSON exige uma chave própria para cada texto (`t0`, `t1` etc.), todas obrigatórias e sem chaves extras. O plugin confere quantidade, duplicações, conteúdo incompleto, recusas e marcadores do jogo antes de salvar. Os nomes de NPC no cabeçalho da HUD continuam excluídos da tradução; o modelo também recebe instruções para preservar nomes dentro das frases.

Se uma resposta chegar fora do formato, o preparador divide o lote e tenta automaticamente com grupos menores, salvando cada grupo concluído antes de continuar. Há no máximo seis chamadas extras de recuperação imediata por lote; um texto individual pode ser tentado novamente uma vez, com mais espaço de saída. Os originais restantes viram pendências individuais após esse limite, sem parar os outros lotes e sem serem marcados como salvos. Recusas também deixam os originais pendentes, sem gravar a recusa como tradução. Todas as tentativas respeitam RPM/TPM e a prioridade dos NPCs. Falta de saldo e problemas de acesso exigem correção antes de retomar.

`plugin\diagnostico-openai.log` registra somente códigos de falha, quantidades e o identificador da requisição, para ajudar a investigar erros intermitentes. Não grava a chave, o texto do jogo ou a resposta bruta. **Tentar novamente após erro** agora limpa os erros e retoma também a preparação geral pausada; não é necessário clicar em dois botões.

RPM e TPM são tetos, não metas de velocidade. O controle é compartilhado pelas duas filas de idioma e mantido ao aplicar novamente o tradutor. Ele reserva cada tentativa, inclusive novas tentativas, por uma janela móvel de 60 segundos. A reserva de tokens inclui uma estimativa conservadora da entrada em UTF-8, instruções, formato JSON e a saída máxima. Isso pode limitar a velocidade antes do teto exato da conta. A contagem local reinicia quando o plugin é descarregado; os limites do serviço continuam valendo. Outras aplicações que usam a mesma conta também podem consumir esses limites.

Os [cabeçalhos de limite e Retry-After](https://developers.openai.com/api/docs/guides/rate-limits) informados pela API são respeitados, inclusive limites menores do modelo ou projeto. Ao atingir o limite local, a fila aguarda automaticamente sem bloquear a thread do jogo. A preparação cede a vez aos NPCs entre lotes e durante essas esperas; uma chamada já enviada pode terminar primeiro.

Erros temporários 429/503 e outros erros transitórios têm até três tentativas imediatas, com espera crescente ou `Retry-After` e até 90 segundos para essa recuperação. Se continuarem falhando ou a espera for maior, os originais entram nas novas tentativas posteriores automáticas. Chave inválida, modelo sem acesso e falta de saldo pausam as solicitações com uma mensagem. Confira a conta e use **Tentar novamente após erro**. Lotes concluídos permanecem no TXT, mesmo quando uma chamada posterior falha. A chave OpenAI nunca é enviada aos outros provedores; o fallback Google continua exclusivo do DeepL.

Selecionar o Luna não refaz traduções existentes. Correções manuais, leitura instantânea do TXT, nomes no cabeçalho, tamanho das caixas e preparação por expansão permanecem no fluxo existente. O provedor anterior permanece selecionado até você salvar a mudança. Recarregar o plugin pode interromper uma chamada em andamento: uma requisição já recebida pelo serviço ainda pode consumir saldo.

## Arquivos que devem ser guardados

- `plugin\traducoes.txt`: UTF-8, original + TAB real + português. É o arquivo principal de leitura.
- `plugin\traducoes.txt.bak`: cópia de recuperação, atualizada com os resultados. Se o TXT for excluído, a cópia o restaura na próxima abertura.
- `plugin\progresso-preparacao.json`: posição confirmada no catálogo; não contém chave.
- `plugin\pendencias-ja.json` e `plugin\pendencias-cliente-IDIOMA.json`: originais ainda sem tradução válida, com contagem e prazo para nova tentativa automática.
- `plugin\originais-japoneses.jsonl`: catálogo local com a ordem de preparação.
- `plugin\fontes-English-JA.jsonl`: correspondências entre os textos em inglês e os originais japoneses. Pode ser reconstruído a partir da instalação.
- `plugin\chave.dpapi`: chave protegida, necessária apenas durante a preparação com DeepL.

Dentro do TXT, quebras de linha aparecem como `\n`, tabulações como `\t` e barras invertidas como `\\`. Linhas iniciadas por `#` são comentários. Se houver entradas repetidas, a última vale.

Use **Abrir arquivo de texto** para corrigir o português, salve e use **Recarregar arquivo** ou `/brisa recarregar`. Pause a preparação enquanto edita. Uma correção humana tem prioridade sobre respostas em andamento.

A cópia `.bak` protege contra excluir o TXT isoladamente. Para sobreviver à perda do disco ou da pasta inteira, guarde a pasta do plugin também em outro disco ou backup de sua escolha.

## Remover os tradutores quando tudo estiver pronto

Ao concluir todas as entradas, a preparação para e a HUD continua lendo o TXT sem novas traduções automáticas.

Aparecerá o botão **Remover os tradutores: gerar versão final somente de leitura**. Ele executa `Gerar-Somente-Leitura.ps1`, que:

1. Confere se cada entrada do catálogo realmente existe no TXT.
2. Compila uma versão em que o código dos serviços online é excluído.
3. Guarda a DLL anterior em `plugin\backup-preparador-...` e troca a DLL no mesmo caminho já registrado.
4. Preserva o TXT, sua recuperação e os índices.

Confira `plugin\conversao-final.log` e recarregue Brisa XIV em `/xlplugins` após a confirmação de sucesso. A conversão não pode ser feita antes de terminar o catálogo. A versão final não traduz textos novos; se um patch acrescentar conteúdo, será necessário voltar ao preparador para ampliá-lo.

## Falas de uma missão específica antes de jogá-la

Durante a preparação, você também pode buscar a missão pelo nome em inglês, japonês ou ID, selecionar e usar **Pré-traduzir linhas novas**. Isso permite antecipar aquela missão. O preparador geral reaproveita essas entradas e não as traduz outra vez.

## HUD e cobertura prática

As traduções são aplicadas no texto nativo, com suporte aos marcadores comuns de cor, borda, negrito, itálico e quebra de linha. A tradução pode alterar a posição da ênfase dentro de uma frase. A camada preta sobreposta foi removida. Campos com ícones, links ou expressões incompatíveis mantêm o original e podem ser consultados no painel `/brisa ler`.

O plugin mede as linhas com a fonte do jogo, quebra palavras pela largura disponível e amplia a altura de caixas de fala identificadas quando necessário. O fundo cresce para cima, mantendo a borda inferior no lugar. A caixa principal Talk e os balões MiniTalk têm tratamentos próprios, pois seus fundos são diferentes. A geometria anterior é restaurada ao trocar a fala, desativar a HUD ou descarregar o plugin.

Caixas de entrada, correio, chat e nomes sobre os personagens ficam fora da captura genérica. Não execute outro plugin de tradução modificando as mesmas caixas simultaneamente.

Preparar todos os campos japoneses legíveis não garante substituir todo texto visível: correspondências ambíguas, expressões com valores dinâmicos, texto desenhado como imagem e elementos fora dos nós de texto podem continuar no idioma original. Nomes personalizados e mensagens de jogadores não existem previamente no catálogo. Contêineres desconhecidos e falas que excedam os limites de ampliação podem exigir o painel ampliado. A substituição genérica e a aparência do redimensionamento ainda precisam ser conferidas no jogo.

## Compilar, verificar e desativar

`Compilar.ps1` usa .NET 10 e as referências locais do Dalamud. Neste computador encontra o SDK portátil preparado em `work\dotnet`. Os testes não usam chaves reais nem rede. Veja `VALIDACAO.md` para o que foi confirmado e os limites dos testes.

Para desativar, desmarque Brisa XIV em `/xlplugins`. Para remover o registro, remova o caminho em **Dev Plugin Locations**. Guarde o TXT e sua cópia. Nenhum arquivo da instalação do FFXIV é substituído.

## Referências

APIs: [Dalamud](https://dalamud.dev/), [Lumina](https://github.com/NotAdam/Lumina), [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) e [DeepL](https://developers.deepl.com/docs/best-practices/error-handling). Este projeto não representa autorização da Square Enix para ferramentas de terceiros.

## Escolher quais textos aparecem traduzidos

Abra **O que traduzir** na navegação ou use `/brisa categorias`. Falas, legendas, missões, itens, habilidades, títulos de janelas, menus, confirmações, avisos e outras janelas têm opções separadas. Passe o mouse sobre **?** para ler o alcance de cada opção. As escolhas são salvas automaticamente; também há botões para ativar ou desativar todas.

Títulos de janelas controla os cabeçalhos de componentes padrão nas janelas habilitadas. Títulos de missões seguem Missões; nomes em descrições de itens seguem Itens. Nomes de NPC continuam preservados. Desativar uma categoria não apaga o TXT, não altera a preparação e não envia novamente traduções salvas.
### Subdivisão de título e descrição — 0.8.4

Na aba O que traduzir, expanda uma categoria em Título e descrição por categoria. As opções individuais prevalecem sobre o padrão Títulos de janelas; a categoria principal também precisa estar habilitada. Itens usa o campo ItemNameText; missões usa DutyNameTextNode em JournalDetail e QuestTitleText em JournalAccept. Cabeçalhos padrão de janela também são identificados. Nas outras telas, campos sem identificação segura continuam em Descrição e demais textos, que inclui rótulos e botões. Não há divisão baseada no tamanho ou conteúdo da frase.
### Nomes, locais e atividades — 0.8.4

Nomes de personagens (NPCs) permite consultar traduções já salvas para campos identificados de nome nas conversas e janelas habilitadas. Desligado por padrão; placas flutuantes, jogadores e chat continuam excluídos. Não envia nomes à API e mantém o original se não houver tradução disponível.

Locais e viagens controla anúncios de área e Teleport. Receitas e fabricação controla RecipeNote e RecipeTree. Conquistas e coleções controla Achievement, MountNote, MinionNote, FishGuide e AdventureNoteBook. As três opções herdam as escolhas antigas de menus/avisos até serem alteradas. Também têm a subdivisão entre cabeçalhos identificados e descrição/demais textos. Outras telas sem classificação permanecem nas categorias anteriores.
## Atualização 0.8.4 — varredura rápida e diário

O controle Traduzir diário de missões em O que traduzir liga ou desliga Journal e JournalDetail, preservando aceitação, conclusão e objetivos. Também é possível alternar com `/brisa jornal`. A escolha fica salva automaticamente e herda Missões antes da primeira alteração. Títulos e descrições seguem a subdivisão de Missões; os textos permanecem no TXT.

A preparação percorre entradas já salvas com orçamento de 2 ms e teto de 8.192 entradas por passagem. Se atingir o orçamento, retoma em cerca de 16 ms, independentemente do tamanho do lote DeepL. A procura à frente também pula traduções salvas sem gastar vagas de lote. Limites de API, prioridade da HUD, tentativas e confirmação de conclusão continuam em vigor. O progresso é gravado no máximo uma vez por segundo durante a varredura; a conclusão é gravada imediatamente após a auditoria. Recarregar pode repetir apenas consultas locais desde o último checkpoint.
## Descrição rolável do Journal — 0.8.4

Textos na área rolável identificada de JournalDetail passam a usar a quebra de linha nativa, mantendo dimensões e limites do painel. Eles não passam pela regra de crescimento de balões que rejeita conteúdo longo em contêiner desconhecido. O reconhecimento usa o ancestral Res de ID 40, conferido no ULD local. Desativar a tradução restaura os flags originais. A extensão da rolagem e a renderização final precisam ser conferidas no jogo após recarregar o plugin.