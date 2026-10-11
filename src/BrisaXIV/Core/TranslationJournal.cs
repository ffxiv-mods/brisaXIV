using System.Text;

namespace BrisaXIV.Core;

// A separate copy, in the TXT format, of the translations created while playing.
// Incorporar-Traducoes-Novas.ps1 merges it into the TXT published with the plugin.
public sealed class TranslationJournal(string path)
{
    private readonly object gate = new();
    public string Path { get; } = path;
    public string? Warning { get; private set; }

    public void Append(string source, string translation)
    {
        lock (gate)
        {
            try
            {
                if (!File.Exists(Path))
                    File.WriteAllText(Path,
                        "# BrisaXIV — traduções novas criadas durante o jogo, no mesmo formato do traducoes.txt.\n# Use Incorporar-Traducoes-Novas.ps1 para levá-las ao TXT publicado.\n",
                        new UTF8Encoding(false));
                File.AppendAllText(Path, TextRules.Escape(TextRules.Normalize(source)) + "\t" + TextRules.Escape(translation) + "\n", new UTF8Encoding(false));
                Warning = null;
            }
            catch (IOException) { Warning = "Não foi possível gravar a cópia separada das traduções novas."; }
            catch (UnauthorizedAccessException) { Warning = "Sem permissão para gravar a cópia separada das traduções novas."; }
        }
    }
}
