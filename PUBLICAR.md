# Publicar pelo navegador e instalar no experimental

A pasta preparada contém dois destinos: **repositorio**, com os arquivos para o GitHub; e **release**, com o ZIP que o Dalamud instala. Enviar um ZIP comum como arquivo de código não o transforma automaticamente num plugin instalável.

## 1. Enviar o projeto ao GitHub

1. Abra [BrisasXIV/BrisasXIV](https://github.com/BrisasXIV/BrisasXIV) na conta proprietária.
2. Use **Add file → Upload files**. Entre na pasta **repositorio** preparada e arraste seu conteúdo. `repo.json`, `README.md`, `src`, `tests`, `plugin` e `data` devem ficar na raiz do GitHub, sem uma pasta externa chamada `repositorio`.
3. Confira que os scripts `.ps1`, `.gitignore` e demais arquivos da raiz também foram enviados. Confirme o commit na branch **main**.

O TXT grande é compactado em `data`; quando necessário, o preparador o divide em partes de até 20 MiB. Os hashes permitem conferir a recomposição. O script `Restaurar-TXT-Inicial.ps1` recupera esse TXT para uma instalação de desenvolvimento sem substituir um arquivo existente. O limite de envio pelo navegador é [25 MiB por arquivo e até 100 arquivos por envio](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository?platform=windows). Se usar `BrisaXIV-fontes-para-GitHub.zip`, **extraia-o** e envie os arquivos extraídos para manter o código navegável.

## 2. Criar a release que o Dalamud baixará

1. No GitHub, abra **Releases → Draft a new release**.
2. Crie a tag exata **v0.8.4**, apontando para `main`. Título sugerido: **Brisa XIV 0.8.4 — experimental**.
3. Copie `release/NOTAS-DA-RELEASE.txt` para a descrição.
4. Anexe **release/latest.zip**, mantendo exatamente esse nome. Pode anexar também `SHA256.txt`.
5. Marque como **pré-lançamento / pre-release** e publique.

Não anexe `.dpapi`, configurações da conta, logs, pendências, progresso, `uso-ia.jsonl`, `custos-ia.json` nem pastas de dados do Dalamud. O preparador seleciona explicitamente os arquivos e confere possíveis segredos antes de indicar sucesso. Não envia nada à internet.

## 3. Adicionar o repositório ao Dalamud

Após concluir os dois passos anteriores, use este endereço:

```text
https://raw.githubusercontent.com/BrisasXIV/BrisasXIV/main/repo.json
```

1. Abra `/xlsettings`, vá à seção **Experimental** e adicione a URL em **Custom Plugin Repositories / Repositórios personalizados**.
2. Ative a opção para receber plugins de teste/experimentais. O manifesto usa `IsTestingExclusive: true` e `TestingAssemblyVersion: 0.8.4.0`.
3. Salve, abra `/xlplugins`, atualize a lista e busque **Brisa XIV**. Instale a versão de teste.
4. Se estiver usando a DLL de desenvolvimento, desative essa instância antes de instalar a versão do repositório. Preserve sua pasta atual. Para continuar seu trabalho, copie o TXT mais recente e `progresso-preparacao.json` para a pasta persistente da instalação normal **com o Brisa descarregado**. Copie também os índices e `pendencias-*.json` se quiser reaproveitá-los. Esses arquivos permanecem locais.
5. Abra `/brisa`. Na instalação normal, o arquivo ativo fica em `%APPDATA%\XIVLauncher\pluginConfigs\BrisaXIV\traducoes.txt`; a página **Arquivo TXT** mostra o caminho exato. A primeira instalação importa o TXT do pacote. Atualizações seguintes preservam os dados existentes.

A estrutura do manifesto segue a [documentação de repositórios personalizados do Dalamud](https://dalamud.dev/plugin-publishing/custom-repositories/). A URL só estará disponível depois do commit e da release. Esta preparação local não publica o projeto nem altera suas contas.

O repositório e a release precisam estar acessíveis publicamente: a lista de plugins do Dalamud usa HTTP sem autenticação. O ZIP inclui somente os binários e o TXT autorizado.

## Próximas versões

Compile, teste e execute `Preparar-Publicacao.ps1` novamente. A DLL e seu manifesto devem ter a mesma versão. O script gera a nova tag e os três links de download; publique a nova release e substitua `repo.json` no GitHub. Mantenha `IsTestingExclusive` enquanto o plugin estiver em testes.

```powershell
.\Preparar-Publicacao.ps1
```

Não use a pasta de dados pessoal como repositório. Envie o conteúdo da nova pasta **repositorio** selecionada pelo preparador.
