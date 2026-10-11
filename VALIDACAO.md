# Validação do Brisa XIV

## Atualização 0.8.0 — categorias e confirmações

Verificada em 11 de outubro de 2026. A aba O que traduzir reúne dez opções, ativação/desativação conjunta e tooltips explicativos. A migração preserva as escolhas anteriores: legendas seguem inicialmente Falas, habilidades seguem Itens e confirmações seguem Menus.

169 verificações automatizadas passaram, incluindo políticas de cabeçalhos, preservação de nomes e restauração da geometria de confirmações. As versões preparadora e somente de leitura compilaram sem erros ou avisos. Prévia nativa ImGui da nova página e de seu tooltip conferida com dados fictícios, sem chamadas à API.

Inclui a correção de crescimento das caixas Sim/Não e OK, com espaço para botões. A renderização e interação finais dessas caixas ainda precisam ser conferidas dentro do jogo após recarregar o plugin. A atualização substitui apenas os binários; TXT, credenciais e progresso são preservados.


Verificado em 10 de outubro de 2026 com o Dalamud local 15.0.3.6 / API 15 e SDK .NET 10.0.401.

## Atualização 0.7.1 — espaçamento da interface

- Margens internas explícitas na navegação e no conteúdo, intervalo maior entre os dois painéis e alinhamento vertical dos itens laterais. Cartões e controles recebem maior preenchimento; tabelas usam 8 px na horizontal e 6 px na vertical em cada célula.
- Separadores com espaço antes e depois, linhas de estatísticas agrupadas com intervalo próprio e barra de progresso de 30 px. A lógica de tradução e persistência permanece igual.
- Compilações preparadora e somente de leitura passaram sem erros ou avisos. Prévia ImGui com dados simulados conferiu a navegação, cartões de custos e os blocos superiores do progresso, sem chamada à API. Os filtros de texto continuam exigindo o backend do Dalamud: para a prévia do progresso, foram omitidos somente numa cópia temporária do código, nunca na DLL entregue.
- TXT, progresso, índices, chaves e pendências não são substituídos pela atualização. A escala de fonte e a interação precisam ser conferidas no jogo depois de recarregar a DLL.

## Atualização 0.7.0 — interface, custos e alerta de saldo

- Janela com navegação lateral, tema escuro/roxo, cartões e páginas que mantêm as funções existentes. Custos da IA aparece para OpenAI/Ollama configurado e aplicado. `/brisa custos` abre essa página; sem IA configurada, abre Tradutor.
- Registro por chamada compartilhado pelas filas: tokens informados de entrada, cache, escrita de cache e saída, horário UTC, modelo e nível de serviço. Estimativa por preços editáveis, sem confundir reservas TPM com faturamento. Respostas rejeitadas também são contadas; respostas sem uso, interrupções, modelos e níveis sem tarifa ficam desconhecidos. O TXT antigo não é reenviado para reconstruir custos.
- Consulta administrativa independente ao Costs API, com chave protegida separada, filtros opcionais, paginação completa e respeito a Retry-After. Ausência/zero não prova gratuidade. O escopo conta/projeto/chave é mostrado e pode incluir outros aplicativos. Cobertura gratuita permanece não informada. Um identificador aleatório de configuração impede reaproveitar um relatório salvo depois de mudar a credencial.
- Alerta específico para créditos esgotados, gasto/cota atingidos ou faturamento inativo. Pausa compartilhada imediata entre as duas filas, inclusive lotes que ainda aguardam limite e não foram enviados. Chamadas já em andamento podem terminar. Texto salvo continua disponível. Limites temporários de requisições continuam com espera automática; não disparam alerta de falta de crédito.
- **154 verificações automatizadas passaram**, com HTTP simulado e arquivos próprios. Incluem os testes anteriores de HUD, concorrência, pendências e catálogo, além de cálculo monetário, cache sem contagem dupla, nível/contexto longo, histórico persistente e corrompido, uso de respostas inválidas, chamadas sem contadores, erros de crédito/cota, pausa do outro lote sem envio/custo inventado, paginação, consulta negada e troca do perfil administrativo.
- Preparador e DLL somente de leitura compilados sem avisos nem erros. A DLL final somente de leitura foi conferida como UTF-8 e UTF-16 e não contém os endpoints dos quatro tradutores.
- Prévia nativa ImGui do gráfico, cartões, tabela de modelos e alerta renderizada fora do jogo com **dados fictícios**, disponível em `docs`. Uma tentativa desse ambiente mínimo com campos de edição expandidos falhou na biblioteca nativa; ele não reproduz o backend completo do Dalamud. Interação com credenciais, escala/fonte do jogo e captura de HUD ainda precisam de teste dentro do FFXIV. Não houve consulta à conta real nem chamada paga nos testes.
- Exportação por lista explícita: **66 arquivos**, todos abaixo de 25 MiB para o navegador. ZIP instalável com DLL, manifesto, dependências e TXT na raiz. Manifesto exclusivo de teste com versão 0.7.0.0, API 15 e download na tag v0.7.0. Sem PDBs, credenciais, configuração, logs, índices, progresso, pendências ou dados de custos na exportação. Binário também conferido sem caminho pessoal do desenvolvedor.
- TXT inicial compactado restaurado em arquivo de teste e comparado pelo SHA-256: **68.146.580 bytes**, exatamente a fotografia publicada. Um arquivo existente com correção pessoal foi preservado. O exportador compartilha leitura/escrita com a instância ativa e omite uma eventual última linha incompleta.
- Instalação normal usa a pasta persistente do Dalamud e importa o TXT inicial somente na primeira vez; desenvolvimento mantém o caminho atual. A atualização copia somente os quatro binários, com cópia de recuperação; TXT, índices, pendências, progresso e credenciais existentes são preservados.
- O GitHub e os downloads não foram publicados ou testados remotamente. O procedimento está em `PUBLICAR.md`; o endereço experimental só funcionará após enviar os arquivos e publicar `latest.zip` na release correspondente.

## Atualização 0.6.1 — pendências sem interromper a fila

- O diagnóstico local mostrou rejeições `empty-or-control-text` e `placeholders`. A recuperação imediata continua limitada e valida os marcadores antes de salvar. Ao esgotá-la, os originais ainda sem resultado válido são separados em pendências individuais; os outros lotes continuam. Resultados já confirmados no TXT são preservados. Recusas, falhas temporárias, limites transitórios e problemas de conexão também têm tentativa posterior, sem serem gravados como tradução pronta.
- A nova tentativa acontece após mais 50 textos salvos. Falhas repetidas aumentam essa distância para 100, 200, 400 e no máximo 500. Se não houver mais trabalho executável, um intervalo crescente de 30 segundos até 1 hora permite recuperar as últimas pendências. O temporizador não antecipa a contagem enquanto outros textos estão em andamento. Esperas mínimas do servidor prevalecem mesmo com contagem atingida ou botão manual; a pausa compartilhada de RPM/TPM continua ativa.
- As pendências persistem em arquivos por fila, sem credenciais ou respostas brutas. O registro permanece enquanto a tentativa está em andamento e só é removido após confirmar o TXT. Reaplicar o tradutor transfere a posse desse arquivo: a limpeza tardia de trabalhadores cancelados não sobrescreve os registros da nova fila. Entradas já presentes no TXT são descartadas das pendências sem nova chamada.
- O preparador compara incrementalmente todos os originais do catálogo com o TXT, incluindo posições anteriores ao cursor salvo. Repara entradas ausentes mesmo quando a contagem do TXT não diminuiu. A conclusão exige conferência completa e nenhuma ausência; recarregar um TXT editado invalida a conclusão anterior. O formato do catálogo, do TXT e do progresso permanece compatível.
- A aba Progresso mostra **Pendentes — nova tentativa depois**, o número restante de textos para cada tentativa, o intervalo alternativo e a conferência de todos os originais. Inclui **Conferir se falta alguma tradução**. Pré-tradução de missão também continua aguardando pendências e não declara conclusão apenas porque a fila executável esvaziou.
- Chave inválida, modelo indisponível, falta de saldo e impossibilidade de gravar o TXT continuam exigindo correção antes de retomar solicitações. Uma falha de gravação não é marcada como concluída nem dispara cobrança automática contínua. O botão de retomada funciona na mesma sessão depois da correção.
- **116 verificações automatizadas passaram**, usando arquivos próprios, respostas HTTP simuladas e relógio controlado, sem API real nesta atualização. Incluem continuação dos 99 outros originais após uma falha persistente, nova tentativa pela contagem sem avançar o relógio, recuperação da última pendência por intervalo, persistência e transferência entre filas, espera mínima da API, falha de gravação, ausência anterior ao cursor, contagem igual com chave removida, revalidação após edição, além dos testes anteriores de concorrência, prioridade da HUD e provedores.
- Preparador e versão somente de leitura compilaram sem avisos nem erros. A DLL somente de leitura foi examinada como UTF-8 e UTF-16 nos dois alinhamentos e não contém os endpoints OpenAI, DeepL, Google ou Ollama. A aparência da nova indicação de pendências ainda precisa ser conferida dentro do jogo; os testes não reproduzem o renderizador nativo.
- Os quatro binários preparadores 0.6.1.0 foram copiados para a pasta já registrada. Binários anteriores guardados em `work/retry-build/backup-binarios-20261009-182308`. O TXT manteve exatamente **10.029.062 bytes** e o mesmo SHA-256. O progresso permaneceu em **Cursor 38.657 / SavedCount 38.856**, com o mesmo identificador do catálogo. TXT, índices, progresso e chaves pessoais não foram substituídos.
- Para carregar esta DLL, desative e ative Brisa XIV uma vez em `/xlplugins`. As novas tentativas posteriores são automáticas; não é necessário reiniciar o jogo.

## Atualização 0.6.0 — progresso e lotes simultâneos

- Adicionada a aba **Progresso**, aberta também por `/brisa progresso`, com totais permanentes do TXT e contadores da sessão separados. Inclui textos, caracteres originais/portugueses, palavras, progresso confirmado do catálogo, grupo/expansão, etapas, filtros e detalhes dos textos e requisições recentes.
- O envio só é contado imediatamente antes de iniciar a chamada HTTP. Selecionar um lote, esperar uma vaga ou aguardar RPM/TPM não aumenta envios. Respostas recebidas/validadas são distintas de resultados efetivamente confirmados no TXT. Novas tentativas e fragmentos longos contam no volume enviado; originais salvos são contados uma vez. Não se inventa histórico de API para entradas antigas.
- Caracteres são valores Unicode e palavras são unidades separadas por espaços contendo letras/números. Os totais são atualizados após gravar e recalculados ao recarregar o TXT, considerando a última correção humana de cada original. O índice do catálogo mantém uma soma por posição para mostrar caracteres confirmados sem recontar centenas de milhares de entradas na interface.
- Luna processa quatro lotes de preparação simultâneos por padrão, ajustáveis de um a 32, com uma vaga adicional para a HUD. As duas filas compartilham concorrência, RPM e TPM. A vaga de HUD existe também na configuração de um lote; atividade prioritária impede iniciar novos lotes de fundo em ambas as filas. Chamadas já enviadas podem terminar. DeepL, Google e Ollama mantêm o processamento anterior.
- A fila conserva deduplicação sob bloqueio, e as gravações no TXT continuam sincronizadas. O preparador usa uma janela maior e alimentação a cada 250 ms no modo simultâneo. Cursores só avançam sobre registros confirmados no TXT; os formatos de TXT, catálogo e progresso não mudaram.
- O painel mostra requisições nos últimos 60 segundos e reservas estimadas de tokens do limitador. Estas reservas não são consumo faturado. Limites ativos são lidos do tradutor aplicado, preservando a precisão mesmo se o usuário editar opções sem aplicar. Histórico e contadores de API pertencem à instância do plugin; totais permanentes vêm do TXT.
- **98 verificações automatizadas passaram**, sem API real ou chave real nesta atualização. Os novos testes cobrem Unicode, palavras, correções e reabertura, fila versus envios reais, respostas em espera, gravação confirmada, fragmentos, recuperação e volumes repetidos, concorrência compartilhada entre idiomas, deduplicação, vaga de NPC com todos os lotes ocupados, configuração de um lote, prioridade global, RPM sob concorrência, cancelamento e alimentação ampliada. As verificações anteriores de HUD, nomes, DeepL/Google, retomada, layout e saída estruturada continuam passando.
- Compilações preparadora e somente de leitura verificadas separadamente sem avisos nem erros. A versão somente de leitura é conferida para ausência de endpoints online. A nova aba ainda precisa de confirmação visual dentro do jogo após recarregar o plugin; os testes simulam o processamento e não medem a latência real do Luna. Não há promessa de atingir 5.000 RPM: tokens, tamanho dos lotes, tempo de resposta e limites remotos continuam controlando a velocidade, conforme a [documentação oficial da OpenAI](https://developers.openai.com/api/docs/guides/rate-limits).
- DLL e manifesto 0.6.0.0 copiados para a pasta já registrada, com cópia dos quatro binários anteriores em `work/progress-build`. O TXT manteve exatamente seus 6.334.680 bytes e o mesmo SHA-256; o progresso permaneceu no cursor 24.926, SavedCount 25.006, com o mesmo identificador do catálogo. Nenhum arquivo pessoal de tradução, índice, progresso ou chave foi substituído.

## Atualização 0.5.1 — recuperação do Luna e espera visível na HUD

- A mensagem mostrada na imagem correspondia à rejeição de uma resposta JSON. O lote pendente no grupo `quest/052/KinGmh101_05299` passou em uma chamada real com o código antigo, indicando uma falha intermitente. A resposta original rejeitada não havia sido registrada; sua causa exata não pôde ser recuperada.
- O schema antigo permitia uma lista de tamanho arbitrário e IDs repetidos. A saída agora exige uma chave individual para cada texto, todas obrigatórias e sem chaves extras, conforme o formato estruturado documentado pela OpenAI.
- Quando a validação falha, o lote é dividido automaticamente, com até seis chamadas extras de recuperação. Grupos concluídos são salvos antes de recuperar o restante. Textos individuais têm no máximo uma nova tentativa; falhas persistentes pausam a preparação sem pular entradas ou entrar em um ciclo de cobrança ilimitado.
- Acrescentado diagnóstico local com códigos fixos, quantidades e request ID validado. Não contém texto original, tradução bruta, chave ou corpo de requisição. Recusas da API continuam pausando sem recuperação de formato.
- O botão **Tentar novamente após erro** limpa os erros das filas e retoma a preparação que estava pausada. Aplicar outro tradutor também retoma o preparador.
- Removida a alteração da transparência da raiz das caixas de diálogo. A espera mantém a janela visível e escreve **Traduzindo…** no próprio campo de fala. O resultado usa o original guardado e substitui o indicador automaticamente. Os prazos pertencem a cada nó, sem interferência entre balões.
- A perda de visibilidade durante fechamento não restaura o original. Uma atualização do jogo com o mesmo original mantém a tradução aplicada; nova fala ou reutilização do nó invalida o resultado anterior. Desativar a tradução/HUD ou descarregar ainda restaura os dados originais.
- **79 verificações automatizadas passaram**, incluindo schema com chaves obrigatórias, recuperação em grupos menores, resultados parciais após falha persistente, diagnóstico sem dados privados, política de fechamento, independência dos prazos dos balões e as verificações anteriores.
- Um lote real de 20 textos ainda não salvos foi enviado ao Luna com o novo formato: HTTP 200, status completed e 20 traduções válidas. O teste leu a chave protegida já configurada e não gravou o TXT ou o progresso pessoal. O preparador aberto continuou avançando durante a investigação; o teste novo foi no grupo `quest/052/KinGra205_05293`, cursor 24.006.
- A compilação preparadora e a compilação somente de leitura foram verificadas separadamente. A aparência e a animação de fechamento precisam ser conferidas no jogo após recarregar a DLL; os testes não reproduzem o renderizador nativo do FFXIV.

## Atualização 0.5.0 — OpenAI GPT-6 Luna

- Acrescentado o provedor OpenAI sem mudar os valores numéricos dos provedores existentes. Modelo padrão `gpt-6-luna`, 5.000 RPM e 2.000.000 TPM, configuráveis em `/brisa`.
- A chave OpenAI tem campo próprio, é protegida por DPAPI na configuração e é enviada somente no cabeçalho Bearer para `https://api.openai.com/v1/responses`. A chave DeepL não é reaproveitada nem enviada à OpenAI.
- As filas japonesa e do idioma do cliente compartilham a mesma janela móvel de RPM/TPM. Reaplicar o tradutor mantém as reservas. A contagem local não persiste após descarregar o plugin; os limites remotos continuam sendo respeitados.
- Cada tentativa reserva uma estimativa conservadora de tokens de entrada, instruções, schema e saída máxima. Lotes são reduzidos para caber em TPM menores. Cabeçalhos de limite do modelo/projeto, saldo restante, resets e `Retry-After` são considerados.
- A Responses API recebe JSON estruturado com IDs, `reasoning.effort=none`, `store=false` e sem ferramentas. A saída é conferida antes da gravação: IDs faltando/repetidos, truncamento, recusas, JSON inválido e mudanças nos marcadores do texto são rejeitados.
- Erros temporários têm novas tentativas limitadas com backoff; falta de saldo, chave inválida ou modelo indisponível pausam a fila. Esperas podem ser canceladas ao descarregar, e a preparação cede a vez a falas prioritárias. Os lotes já confirmados permanecem no TXT após falha posterior.
- O preparador e a DLL somente de leitura compilaram em staging sem erros nem avisos. A DLL somente de leitura foi verificada para ausência dos endpoints OpenAI, DeepL, Google e Ollama.
- **73 verificações automatizadas passaram**, com serviços simulados e relógio virtual para as esperas: incluem o novo provedor, os limites solicitados, cabeçalhos menores, isolamento da chave, validade do JSON, persistência, prioridade e os comportamentos anteriores de DeepL/Google, HUD, nomes, caixas e preparação contínua.
- Não houve chamada real à OpenAI: nenhuma chave OpenAI foi fornecida nesta atualização. Conexão, acesso ao modelo, cobrança e qualidade real da tradução precisam ser verificados com o botão de teste após configurar a chave no plugin.
- O TXT pessoal, os índices e o progresso não foram reescritos pela atualização. Somente os binários e o manifesto são substituídos; as configurações e a seleção do provedor só mudam quando o usuário aplica a opção no `/brisa`.

## Atualização 0.4.0 — HUD nativa, caixas e preparação contínua

- O atraso fixo foi removido para entradas já presentes no `traducoes.txt`: a primeira captura consulta o arquivo imediatamente. A espera de estabilidade continua apenas para texto novo, evitando chamadas por cada etapa da animação de escrita.
- A camada ImGui preta sobre balões formatados foi removida. Cores, bordas, negrito, itálico e quebras de linha compatíveis são preservados no SeString nativo; ícones, links e expressões incompatíveis ficam no jogo original e continuam disponíveis no painel ampliado.
- A fala `Talk`, os balões `MiniTalk` e `BattleTalk` foram conferidos na instalação local. O nome do NPC usa nós separados (Talk 2, BattleTalk 4) e não é traduzido. Nomes de NPCs próximos também são ignorados pela captura.
- A caixa `Talk` mede as linhas usando a fonte do jogo, quebra palavras e aumenta o contêiner quando a altura traduzida excede o espaço. O estado de largura, altura, posição e flags é restaurado quando a fala muda ou o plugin é descarregado.
- A opção **Aguardar tradução antes de mostrar a caixa de diálogo** permite que uma fala nova apareça já em português, com espera máxima ajustável. Traduções salvas não passam por essa espera.
- A opção **Preparação contínua** permite seguir pelas expansões sem clicar em “Retomar mais uma parte nesta sessão”. Ela mantém a ordem, cede a prioridade para falas e conserva cada resultado salvo no TXT.
- A DLL preparadora foi recompilada como versão 0.4.0 no caminho já registrado no Dalamud. A compilação somente de leitura também foi verificada em staging e não contém os endpoints DeepL ou Google.
- 53 verificações automatizadas passaram: persistência, APIs simuladas, prioridade, modo TXT, wrapping de Unicode, controle de espera, preservação de payloads formatados, nomes dos NPCs, preparação contínua e layout local de Talk/MiniTalk/BattleTalk.
- O TXT ativo e o progresso não foram reescritos pela atualização. No momento desta validação, o catálogo estava em `Cursor 22.876`, `SavedCount 22.906`, `Completed false`; a preparação continua normalmente.

Para aplicar a DLL já compilada, desative e ative **Brisa XIV** em `/xlplugins` ou reinicie o jogo. Se a instância anterior ainda estiver carregada, a aparência antiga continuará até a recarga.

## Histórico 0.3.1 — prioridade de NPCs

- Compilação do preparador concluída sem avisos nem erros.
- As 38 verificações da suíte passaram. Ela verifica também fila de preparação cheia com vagas para falas, prioridade de uma linha já enfileirada, deduplicação após promoção, pausa e retomada do Google entre chamadas, preservação de trechos de textos longos, tradução japonesa de fala em cliente inglês com modo TXT ativo, histórico sem novas requisições e pausa da preparação sem mudar a posição confirmada.
- Todos os testes usam seus próprios arquivos temporários e serviços simulados. A atualização não reescreve o TXT pessoal nem o progresso.
- Foi guardada uma cópia dos binários anteriores, do TXT e do progresso antes da mudança. Durante o trabalho, o TXT ativo passou de 16.329 para mais de 18 mil entradas e o cursor confirmado avançou de 16.327 para mais de 18 mil, mantendo o mesmo identificador do catálogo. A preparação existente continuou funcionando.
- A aparência da HUD da versão 0.3.1 ainda depende de teste dentro do jogo: recarregue o Brisa e converse com um NPC. As verificações automatizadas confirmam o processamento e a persistência, não o desenho das caixas nativas.
- A compilação separada da versão somente de leitura também passou sem avisos nem erros; permanece sem endpoints dos tradutores online. O arquivo de índice japonês e o formato do progresso não foram alterados.

## Histórico da validação 0.3.0

- Compilação Release do preparador concluída sem erros e sem avisos.
- 30 verificações automatizadas de comportamento passaram, usando serviços simulados: persistência e correções no TXT, DPAPI, deduplicação, lotes, recuperação após excluir o TXT, correspondência inglês/japonês, ausência de chamadas na HUD em modo TXT, cota DeepL com alternativa Google, ausência da chave DeepL nas requisições Google, resultados parciais preservados após falha, textos longos, ordem de expansões e retomada após reiniciar.
- 3 verificações adicionais dos dados locais passaram: catálogo completo legível, ordem decrescente de expansão e correspondência japonesa da fala em inglês enviada pelo usuário. Total dessa execução: 33 verificações.
- Foram examinadas 7.912 tabelas, sem exceções de tabela ignoradas nessa instalação. O catálogo contém 533.724 textos japoneses distintos / 19.127.667 caracteres. O índice possui 1.182.605 entradas, incluindo chaves globais e por categoria; essas entradas não equivalem a traduções já prontas.
- Ordem confirmada para textos associados a missões: expansão 5, 4, 3, 2, 1, 0; depois textos compartilhados sem associação explícita. Quantidades por grupo: 33.348, 40.441, 33.810, 34.803, 32.204, 68.989 e 290.129 textos.
- Uma segunda execução carregou os arquivos já preparados, sem examinar tabelas do jogo novamente, e confirmou a mesma ordem e a fala da imagem.
- Uma parte real de até 30.000 caracteres foi preparada pelo DeepL Free. O progresso confirmou 1.146 entradas do catálogo; com a frase de teste anterior, havia 1.147 entradas no TXT.
- A fala de Iyaate da imagem foi consultada no japonês local, traduzida pelo DeepL e salva também. Ao fim desta validação, o TXT contém 1.148 entradas. A fala pode ser usada como teste de leitura ao recarregar o Brisa; não foi verificado seu desenho no jogo.
- O endpoint Google foi testado de verdade com uma frase japonesa curta: HTTP 200 e resultado em português. A troca após HTTP 456 foi validada com resposta DeepL simulada, para não esgotar a cota real de propósito.
- A versão final somente de leitura foi compilada separadamente em uma pasta de verificação, sem erros nem avisos. Seus bytes não contêm os endpoints DeepL/Google. O código dos serviços online é excluído dessa compilação. A DLL ativa continua sendo o preparador, pois o catálogo ainda não foi concluído.
- O script de conversão final foi analisado sintaticamente. Ele exige progresso concluído e confere todas as entradas do TXT antes de substituir a DLL; mantém backup do preparador.
- A chave permanece protegida com DPAPI. Não foi copiada para o código nem para as requisições Google.
- O mesmo caminho privado da DLL continua registrado no Dalamud. O log anterior do Dalamud confirmou o carregamento da versão inicial; a versão 0.3.0 precisa ser recarregada pelo usuário.

## Limites desta validação

O catálogo completo ainda não está traduzido. A preparação continuará em partes quando o plugin for carregado. Não foram conferidos dentro do processo do jogo o desenho da HUD da versão 0.3.0, o alinhamento das camadas sobre campos formatados, o corte de português longo ou todos os tipos de janela.

Ler todos os campos japoneses das tabelas não garante que a captura genérica consiga substituir todos os elementos visíveis. Correspondências ambíguas, valores dinâmicos, imagens e texto fora dos nós capturados permanecem como limitações práticas. Mensagens e nomes personalizados não podem ser pré-traduzidos a partir das tabelas.

Os arquivos e o TXT permanecem locais. Nenhum repositório ou pacote público foi criado.

## Atualização 0.8.1

Controles por categoria para título e descrição, com herança das escolhas anteriores e classificação por campos do SDK local e componentes padrão. Compilações preparadora e somente de leitura sem erros ou avisos. Testes de opções herdadas e explícitas e separação de itens, habilidades e confirmações adicionados. A interação final continua exigindo teste no jogo.

## Atualização 0.8.2

168 verificações automatizadas passaram sem acesso aos serviços de tradução (as quatro verificações opcionais de ULD não foram incluídas nesta execução). Classificação independente de locais, fabricação e coleções testada. Nomes de NPCs são opcionais, desligados por padrão e consultam apenas traduções existentes. Ambas as versões compilaram sem erros ou avisos. Prévia ImGui da aba conferida com dados fictícios. Nenhuma tradução ou progresso existente é substituído.

## Atualização 0.8.3 — 11 de outubro de 2026

171 verificações automatizadas passaram. Teste com 6.000 entradas salvas e duas lacunas antes/depois de trechos em cache confirmou envio apenas das duas traduções ausentes em menos de cinco segundos, sem chamadas reais à API. Conclusão validada após auditoria completa e gravação das lacunas. Controle Journal/JournalDetail testado sem incluir aceitação, conclusão ou objetivos. Ambas as DLLs compilaram sem erros ou avisos. A atualização altera apenas binários; o prefixo do TXT e o progresso são conferidos durante a instalação. Desempenho e interação finais ainda dependem da recarga no jogo.

## Atualização 0.8.4 — 11 de outubro de 2026

174 verificações passaram. Incluem descrição longa na área rolável do JournalDetail, reconhecimento do ancestral Res 40, manutenção das dimensões do viewport e restauração de flags ao desligar. Layout conferido no JournalDetail.uld local: viewport 40, conteúdo 44 e texto 46. Ambas as compilações sem erros ou avisos. Confirmado que a descrição completa da missão For Want of a Memory tem correspondente japonês e tradução no TXT. A renderização, cobertura dos campos dinâmicos e extensão da rolagem ainda exigem teste no jogo após recarga. Nenhuma API foi chamada nos testes.
