# Publicar e instalar pelo Dalamud

A publicação usa **git flow** e é automática. O nome da release no git flow define a versão, no padrão `ReleaseV<maior>.<menor>.<correção>` (ex.: `ReleaseV0.0.1`, `ReleaseV0.0.2`). Não é preciso editar a versão em nenhum arquivo.

Quando a tag `ReleaseV…` chega ao GitHub, o workflow `.github/workflows/release.yml` faz o seguinte:
1. confere se a tag aponta para um commit da **`master`**; se não apontar, recusa;
2. compila a DLL com a versão da tag e com as referências oficiais do Dalamud, e roda os testes;
3. gera `latest.zip` (DLL, manifesto e `traducoes.txt`) e `repo.json`;
4. cria a release com esses dois anexos.

A `develop` nunca gera releases.

## URL para o Dalamud

```text
https://github.com/ffxiv-mods/brisaXIV/releases/latest/download/repo.json
```

1. No jogo, abra `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, cole a URL, marque a caixa de ativação e salve.
2. Abra `/xlplugins`, busque **Brisa XIV** e instale.
3. Abra `/brisa`. Na instalação normal, o TXT ativo fica em `%APPDATA%\XIVLauncher\pluginConfigs\BrisaXIV\traducoes.txt`; a página **Arquivo TXT** mostra o caminho exato. A primeira instalação importa o TXT do pacote. Atualizações seguintes acrescentam somente as traduções novas do pacote e preservam as correções existentes.

Se estiver usando a DLL de desenvolvimento (`Instalar-No-Dalamud.ps1`), desative essa instância antes de instalar pelo repositório. As duas têm o mesmo `InternalName`.

**Requisito:** o repositório GitHub precisa ser **público**. O Dalamud baixa o `repo.json` e o ZIP sem autenticação.

## Publicar uma nova versão

```powershell
git flow release start ReleaseV0.0.2
# (opcional) ajustes finais e commits na branch release/ReleaseV0.0.2
git flow release finish ReleaseV0.0.2
git push origin master develop --follow-tags
```

Acompanhe em **Actions → Release**. Ao terminar, a release `ReleaseV0.0.2` aparece em **Releases**, e o Dalamud oferece a atualização no `/xlplugins`.

- Cada release precisa de um número **maior** que o anterior. O Dalamud só oferece atualização quando a versão sobe.
- O git flow deste repositório está configurado sem prefixo de tag (`gitflow.prefix.versiontag` vazio). Assim, a tag fica exatamente com o nome da release. Em um clone novo, rode `git config gitflow.prefix.versiontag ""` depois do `git flow init`.
- Se a tag for enviada antes da `master`, o workflow falha avisando que o commit não está na master. Envie a `master` e use **Re-run jobs**.
- Não crie releases manualmente pela interface do GitHub. A URL usa a *última release*, e uma release manual passaria a ser a versão distribuída.

## Gerar o pacote localmente (opcional)

```powershell
.\Compilar.ps1 -Version 0.0.2
.\Empacotar-Release.ps1          # gera release\latest.zip e release\repo.json (pasta ignorada pelo Git)
```

Sem `-Version`, vale o `<Version>` do csproj. O script falha se `traducoes.txt` for só o ponteiro do Git LFS (nesse caso, rode `git lfs pull`).

Nunca anexe `.dpapi`, `uso-ia.jsonl`, `custos-ia.json`, logs, pendências, progresso ou pastas de dados do Dalamud. O pacote contém somente os binários e o TXT de traduções.
