# Desenvolvimento

Requisitos: Windows x64, SDK .NET 10, referências compatíveis com Dalamud API 15 instaladas pelo XIVLauncher em `%APPDATA%\XIVLauncher\addon\Hooks\dev`. As bibliotecas do Dalamud são referenciadas localmente e não são redistribuídas. A validação atual usou Dalamud 15.0.3.6 e SDK 10.0.401.

```powershell
.\Compilar.ps1
```

Ou informe um SDK portátil com `-DotnetPath`. O script restaura o TXT inicial compactado, se necessário, compila a DLL em `plugin` e roda os testes. Os testes utilizam arquivos isolados e respostas HTTP simuladas; não exigem chaves reais nem chamam provedores externos. Feche/descarregue o Brisa antes de substituir sua DLL.

- `Plugin.Categories.cs` e `Core/TextCategoryPolicy.cs`: opções de exibição e classificação de cabeçalhos.
- `Plugin.Dashboard.cs`: páginas, navegação, cartões e tema.
- `Plugin.Costs.cs`: gráfico diário, tabela de modelos, preços editáveis e consulta de custos.
- `Core/AIUsage*.cs`: contagem persistente por requisição e consumo informado pelo provedor.
- `Core/OpenAICosts.cs`: GET administrativo independente, filtros e paginação.
- `Core/BillingFailure.cs` e `Plugin.BillingAlert.cs`: classificação fixa de saldo/cota e alerta.
- `Core/TranslationQueue.cs`: lotes, prioridade da HUD, deduplicação e novas tentativas.
- `Core/BackgroundPreparation.cs` e `CatalogVerification.cs`: retomada e conferência completa.
- `GameTextCapture.cs`, `NativeText*.cs`: apresentação na HUD e preservação dos nomes.

`uso-ia.jsonl` armazena somente ID aleatório da chamada, horário, provedor/modelo, nível de serviço, estado e contadores de tokens. Não contém os textos ou segredos. Uma chamada começa como pendente; uso válido é registrado antes de validar a tradução. Sem uso informado ou após interrupção, fica desconhecida. Respostas rejeitadas e novas tentativas podem consumir tokens e entram no custo estimado.

As estimativas usam preços editáveis, com distinção de cache, escrita de cache, saída, nível de serviço e contexto longo do Luna. Modelos/níveis desconhecidos ficam sem estimativa. Não use as reservas conservadoras do limitador TPM para faturamento. Alterar a tabela recalcula o histórico; descontos e créditos particulares não são inferidos.

`custos-ia.json` é um relatório de custos da conta/projeto/chave consultado separadamente. A consulta usa credencial administrativa própria, endpoint oficial fixo, redirecionamentos desativados, períodos UTC, limite de páginas e respeito a `Retry-After`. Não publique esse relatório. Falhas de consulta não interrompem traduções. Custos da conta podem incluir outros programas; um zero informado não garante ausência de cobranças futuras.

As releases são geradas automaticamente pelo workflow `.github/workflows/release.yml`, somente a partir da `master`. Para empacotar localmente, execute `Empacotar-Release.ps1` depois de `Compilar.ps1`. O pacote contém apenas DLL, manifesto e TXT; ficam de fora PDBs, configuração, segredos e dados pessoais. Veja `PUBLICAR.md`.

Na instalação normal, o plugin usa `PluginInterface.GetPluginConfigDirectory()` para dados persistentes. Na instalação de desenvolvimento mantém o diretório da DLL. O TXT do pacote é copiado inteiro na primeira instalação. Em cada atualização, `MergePackageTranslations` acrescenta em segundo plano apenas os originais que o jogador ainda não tem (`TranslationFile.ImportMissing`), uma vez por pacote (`pacote-importado.txt`). Traduções pessoais e correções nunca são substituídas.

## Fallback do Google e traduções novas

- `Core/TranslationRouter.cs`: com `FallbackQueues`, o que o caminho principal não entrega vai para filas do Google Tradutor: textos que resolvem para um original japonês mas faltam no TXT no modo "HUD lê somente o TXT", e textos cujo provedor principal falhou. Texto do idioma do cliente sem original japonês (contadores, nomes de jogadores) só sai depois de uma falha real, nunca por conta própria no modo TXT. Opção: `GoogleForMissing`.
- `HoveredTextSources.cs`: avalia com `ISeStringEvaluator` a descrição da ação, traço ou item sob o cursor, no idioma do cliente e em japonês. As macros (potência, trechos por nível) somem do índice estático, então o tooltip só casa com o original por esse caminho.
- `Core/SourceTextIndex.cs`: busca sem diferenciar maiúsculas para textos de até 80 caracteres (nomes de inimigos são guardados em minúsculas) e escopo `Nomes`, vindo de `BNpcName`, `ENpcResident` e `EObjName`, que resolve nomes ambíguos no resto do jogo.
- `NamePlateTranslation.cs`: nomes sobre a cabeça via `INamePlateGui`. Jogadores, retentores e o chocobo do jogador nunca são lidos.
- `Core/TranslationJournal.cs`: as filas do Google e a do idioma do cliente gravam também `traducoes-novas.txt`, no formato do TXT. Para publicar, rode `Incorporar-Traducoes-Novas.ps1` (lê `pluginConfigs\BrisaXIV` e `plugin\`, acrescenta ao `plugin\traducoes.txt` só originais ausentes; `-Limpar` arquiva a origem) e faça uma release.
