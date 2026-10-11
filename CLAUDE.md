# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Visão geral

Brisa XIV é um plugin Dalamud (API 15, .NET 10, Windows x64) que traduz textos da HUD do FFXIV para português do Brasil. Mesmo com o cliente em inglês, ele consulta os originais japoneses nos dados locais do jogo (Lumina) e traduz a partir do japonês. As traduções ficam num TXT permanente (`traducoes.txt`); um texto já salvo nunca é reenviado aos provedores (OpenAI, DeepL, Google, Ollama). A documentação do projeto é em português: `LEIA-ME.md` (uso), `DESENVOLVIMENTO.md` (código), `VALIDACAO.md` (testes/limitações), `PUBLICAR.md` (release).

## Comandos

```powershell
.\Compilar.ps1                    # restaura o TXT inicial, compila a DLL em plugin\ e roda os testes
.\Compilar.ps1 -DotnetPath <sdk>  # com SDK .NET 10 portátil
```

- Requer as DLLs do Dalamud em `%APPDATA%\XIVLauncher\addon\Hooks\dev` (referenciadas via `DalamudHome`, nunca redistribuídas). O script isola APPDATA/LOCALAPPDATA/NuGet num diretório de scratch em `%TEMP%\BrisaXIV-build`.
- O build escreve direto em `plugin\` (`OutputPath`). Descarregue o plugin no jogo antes de recompilar, senão a DLL fica travada.
- Testes: `tests/` **não** usa xUnit/NUnit. É um executável (`tests/Program.cs`) com uma função `Check(bool, msg)` que lança na primeira falha; os arquivos `*Tests.cs` expõem `Run(Check, scratch)` chamados do `Program.cs`. Não há filtro por teste — o runner inteiro é um único processo, então rodá-lo é aceitável. Para iterar num teste específico, comente temporariamente as chamadas `Run` em `Program.cs`.
  ```powershell
  dotnet run --project tests\BrisaXIV.Tests.csproj -c Release -- <scratchDir>
  ```
  Modos extras do runner: `--verify-complete <pluginDir>`, `--live`, `--screenshot`.
- O projeto de testes compila só `Core/*.cs` e alguns arquivos da raiz (`KeyProtection`, `NativeTextContent`, `NativeTextLayout`, `JapaneseSourceIndex`) — não referencia Dalamud. Código testável deve ficar em `Core/` sem dependência de serviços Dalamud; se criar um novo arquivo fora de `Core/` que precise de teste, adicione-o ao `<Compile Include>` de `tests/BrisaXIV.Tests.csproj`.
- Testes usam HTTP simulado e arquivos isolados; nunca chamam provedores reais.

Outros scripts: `Instalar-No-Dalamud.ps1` (registra `plugin\BrisaXIV.dll` em DevPluginLoadLocations; exige o jogo fechado), `Restaurar-TXT-Inicial.ps1` (recompõe `data/traducoes-iniciais.zip.*` sem sobrescrever TXT existente), `Empacotar-Release.ps1` (gera `release/latest.zip` + `release/repo.json`), `Gerar-Somente-Leitura.ps1` (build final somente leitura). `Preparar-Publicacao.ps1` é o fluxo manual antigo (repo `BrisasXIV/BrisasXIV`), substituído pelo workflow.

**Release (git flow):** `git flow release start/finish releaseV<x.y.z>` cria a tag `releaseV<x.y.z>` (o prefixo de tag do git flow fica vazio). O push da tag dispara `.github/workflows/release.yml`, que:
1. recusa tags fora do padrão ou que não estejam na `master`;
2. baixa o Dalamud de `goatcorp.github.io/dalamud-distrib/latest.zip`;
3. roda `Compilar.ps1 -Version <x.y.z>`, que sobrescreve `<Version>` do csproj;
4. roda `Empacotar-Release.ps1`, que grava a versão da DLL no manifesto do ZIP;
5. cria a release com `latest.zip` + `repo.json`.

A tag é a única fonte da versão em produção. O Dalamud usa `https://github.com/ffxiv-mods/brisaXIV/releases/latest/download/repo.json`. Detalhes em `PUBLICAR.md`.

## Arquitetura

**Fluxo de tradução:**
`Plugin.BeforeDraw` (hook `AddonEvent.PreDraw`) → `GameTextCapture` lê nós de texto dos addons → `TranslationRouter.Get` decide a origem:
1. `TranslationFile` (TXT) tem o texto → usa direto.
2. Texto já em japonês → fila japonesa.
3. `SourceTextIndex`/`JapaneseSourceIndex` resolve o texto em inglês para o original japonês → fila japonesa com o original.
4. Sem correspondência → `clientQueue` (traduz do idioma do cliente).

`TranslationQueue` faz lotes, prioridade (falas/HUD), deduplicação e retentativas (`TranslationRetry`, persistido em `pendencias-*.json`); chama `Translator` (partial: `Translator.cs`, `OpenAITranslation.cs`, `AIUsageObserver.cs`), limitado por `OpenAIRateLimiter` e `TranslationConcurrency`. O resultado é gravado no TXT e reescrito no nó nativo via `NativeTextContent`/`NativeTextLayout` (preservando payloads de cor/ícone e ajustando tamanho das caixas com `TextBoxLayout`).

- `Plugin` é uma partial class dividida por área de UI ImGui: `Plugin.Dashboard.cs` (navegação/tema), `Plugin.Costs.cs`, `Plugin.Progress.cs`, `Plugin.Categories.cs`, `Plugin.BillingAlert.cs`, `Plugin.Storage.cs`. Comando `/brisa`.
- `BackgroundPreparation` + `PreparationCatalog` + `CatalogVerification`: pré-tradução do catálogo inteiro do jogo, com retomada (`progresso-preparacao.json`).
- `Configuration.cs` tem `Version`; migrações ficam no construtor de `Plugin` (`if (config.Version < N)`). Ao adicionar opções que exigem default diferente para usuários existentes, incremente a versão ali.

**Build somente leitura (`BRISA_READ_ONLY`):** `-p:BrisaReadOnly=true` define o símbolo; `Translator.cs`, `OpenAI*.cs`, `AIUsageObserver.cs` ficam sob `#if !BRISA_READ_ONLY` e `ReadOnlyTranslator.cs` fornece um `Translator` alternativo sem HTTP. `Gerar-Somente-Leitura.ps1` falha se a DLL contiver endpoints online. Todo código novo que faça rede precisa ficar dentro de `#if !BRISA_READ_ONLY` e o projeto deve compilar nos dois modos.

## Formato e dados

- `traducoes.txt`: UTF-8, uma linha `original<TAB>tradução`, `#` é comentário, a **última ocorrência vence** (correções são anexadas, não reescritas). Escape/normalização em `TextRules`.
- Em instalação dev os dados ficam ao lado da DLL (`plugin\`); instalada, em `GetPluginConfigDirectory()`. O TXT do pacote só é importado se não houver TXT nem `.bak`.
- `plugin/traducoes.txt` (~100 MiB, resultado da tradução completa) **é versionado via Git LFS** (`.gitattributes`); não remova essa regra nem o tire do Git. Jogadores não usam o repositório: o Dalamud baixa o `latest.zip` anexado à Release (gerado por `Preparar-Publicacao.ps1`).
- O resto de `plugin/` é privado ou gerado e fica ignorado (`*.dpapi` com chaves via DPAPI, `uso-ia.jsonl`, `custos-ia.json`, logs, progresso, pendências, índices `*.jsonl`, `.bak`, DLL/PDB de build). Nunca incluí-los em commits ou pacotes; `publicacao/` (exportações de release) e `work/` também são ignorados.
- Estimativas de custo usam tokens informados pelo provedor × tarifas editáveis — nunca as reservas conservadoras do limitador TPM.

## Versão

Em release, a versão vem da tag git flow. `<Version>` no csproj e `AssemblyVersion` em `BrisaXIV.json` valem só para builds locais. O painel lê a versão da própria DLL (`DisplayVersion` em `Plugin.Dashboard.cs`); não escreva a versão fixa no código. `README.md`/`LEIA-ME.md`/`VALIDACAO.md` citam 0.8.4, numeração anterior ao primeiro release público (releaseV0.0.1).
