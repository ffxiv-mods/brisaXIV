# Publicar e instalar pelo Dalamud

A publicação é automática e acontece **somente pela branch `master`**. O workflow `.github/workflows/release.yml` roda a cada push na `master`. Ele compila o plugin com as referências oficiais do Dalamud, roda os testes e gera o `latest.zip` (DLL, manifesto e `traducoes.txt`) e o `repo.json`. Depois cria a release `v<versão>` com esses dois anexos. A `develop` não dispara o workflow e nunca gera releases.

## URL para o Dalamud

```text
https://github.com/ffxiv-mods/brisaXIV/releases/latest/download/repo.json
```

Ela sempre aponta para o `repo.json` da **última release**, e só a `master` cria releases.

1. No jogo, abra `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, cole a URL, marque a caixa de ativação e salve.
2. Abra `/xlplugins`, busque **Brisa XIV** e instale.
3. Abra `/brisa`. Na instalação normal, o TXT ativo fica em `%APPDATA%\XIVLauncher\pluginConfigs\BrisaXIV\traducoes.txt`; a página **Arquivo TXT** mostra o caminho exato. A primeira instalação importa o TXT do pacote. Atualizações seguintes preservam o TXT existente.

Se estiver usando a DLL de desenvolvimento (`Instalar-No-Dalamud.ps1`), desative essa instância antes de instalar pelo repositório. As duas têm o mesmo `InternalName`.

**Requisito:** o repositório GitHub precisa ser **público**. O Dalamud baixa o `repo.json` e o ZIP sem autenticação.

## Publicar uma nova versão

1. Na `develop`, aumente a versão em **dois lugares**, com o mesmo número:
   - `src/BrisaXIV/BrisaXIV.csproj` → `<Version>0.8.5</Version>`
   - `src/BrisaXIV/BrisaXIV.json` → `"AssemblyVersion": "0.8.5.0"`
2. Faça o merge da `develop` na `master` e dê push.
3. Acompanhe em **Actions → Release**. Ao terminar, a release `v0.8.5` aparece em **Releases** e o Dalamud oferece a atualização.

Se a tag da versão já existir, o workflow não publica nada (aparece o aviso no log). Para republicar sem mudar código, use **Actions → Release → Run workflow** na `master`, depois de aumentar a versão.

Não crie releases ou tags manualmente a partir da `develop`. Como a URL usa a *última release*, uma release manual passaria a ser a versão distribuída.

## Gerar o pacote localmente (opcional)

```powershell
.\Compilar.ps1
.\Empacotar-Release.ps1          # gera release\latest.zip e release\repo.json (pasta ignorada pelo Git)
```

O script falha se a DLL e o manifesto tiverem versões diferentes, ou se `traducoes.txt` for só o ponteiro do Git LFS (nesse caso, rode `git lfs pull`).

Nunca anexe `.dpapi`, `uso-ia.jsonl`, `custos-ia.json`, logs, pendências, progresso ou pastas de dados do Dalamud. O pacote contém somente os binários e o TXT de traduções.
