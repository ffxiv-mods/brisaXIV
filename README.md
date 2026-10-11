# Brisa XIV

Plugin experimental para Dalamud API 15: leitura em português do Brasil na HUD, consultando originais japoneses da instalação mesmo com o cliente em inglês. Traduções ficam num TXT permanente; textos salvos não são enviados novamente à API.

Versão **0.8.4**: passagem rápida por textos salvos e controle independente do diário (/brisa jornal). nova aba **O que traduzir**, com categorias independentes e explicações no botão **?**. caixas de confirmação Sim/Não e OK ajustam o texto e crescem quando necessário, mantendo espaço para os botões. Mantém as margens da interface, progresso, gráfico e histórico de custos/tokens da IA e alerta de saldo/cota esgotados. OpenAI GPT-6 Luna, DeepL, Google e Ollama local.

- [Instalação e uso](LEIA-ME.md)
- [Publicação pelo navegador e repositório experimental](PUBLICAR.md)
- [Compilação e estrutura do código](DESENVOLVIMENTO.md)
- [Validação e limitações dos testes](VALIDACAO.md)

O acompanhamento local mostra **estimativas** calculadas com tokens informados pelo provedor. A confirmação de custos da OpenAI usa uma credencial administrativa separada e opcional. Não atribuímos gratuidade a uma resposta sem uso/custo informado. O relatório da conta pode incluir outros aplicativos.

O pacote inicial contém as traduções autorizadas pelo responsável pelo projeto. Cada usuário fornece suas próprias credenciais dentro do plugin. Chaves, históricos de cobrança, progresso, pendências, logs e índices locais são privados e não fazem parte da publicação. O chat de jogadores não é capturado.

As imagens em `docs` são renderizações da interface ImGui com **dados fictícios**, sem consulta a uma conta real. A aparência final da fonte depende do Dalamud.
