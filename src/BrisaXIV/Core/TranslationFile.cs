using System.Text;

namespace BrisaXIV.Core;

// UTF-8 text, one original/translation pair per line, separated by a real TAB.
// The last occurrence wins, so a correction can be appended without rewriting the file.
public sealed class TranslationFile
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);
    public string Path { get; }
    public string BackupPath => Path + ".bak";
    public string? Warning { get; private set; }
    public int Count { get { lock (gate) return entries.Count; } }
    private TextAmount sourceAmount, portugueseAmount;
    private long generation;
    public long Generation => Interlocked.Read(ref generation);
    public (int Count, TextAmount Source, TextAmount Portuguese) Statistics
    { get { lock (gate) return (entries.Count, sourceAmount, portugueseAmount); } }

    public TranslationFile(string path) { Path = path; Reload(); }

    public void Reload()
    {
        lock (gate)
        {
            try
            {
                var loaded = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!File.Exists(Path) && File.Exists(BackupPath)) File.Copy(BackupPath, Path);
                if (File.Exists(Path))
                {
                    foreach (var line in File.ReadLines(Path, Encoding.UTF8))
                    {
                        if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
                        var separator = line.IndexOf('\t');
                        if (separator < 1) continue;
                        var source = TextRules.Normalize(TextRules.Unescape(line[..separator]));
                        var translated = TextRules.Unescape(line[(separator + 1)..]);
                        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(translated))
                            loaded[source] = translated;
                    }
                }
                else
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    File.WriteAllText(Path,
                        "# BrisaXIV — traduções pessoais, UTF-8.\n# Japonês<TAB>Português do Brasil (use uma tabulação real).\n# Quebras de linha: \\n; tabulações: \\t; barra invertida: \\\\.\n# Para corrigir, edite a tradução e use Recarregar arquivo no plugin.\n",
                        new UTF8Encoding(false));
                }
                entries.Clear();
                foreach (var pair in loaded) entries[pair.Key] = pair.Value;
                sourceAmount = TextAmount.Sum(entries.Keys);
                portugueseAmount = TextAmount.Sum(entries.Values);
                Interlocked.Increment(ref generation);
                Warning = null;
                Backup();
            }
            catch (IOException) { Warning = "Não foi possível ler o arquivo de traduções. Confira as permissões e recarregue."; }
            catch (UnauthorizedAccessException) { Warning = "Sem permissão para ler ou criar o arquivo de traduções."; }
        }
    }

    public bool TryGet(string text, out string translated)
    {
        lock (gate) return entries.TryGetValue(TextRules.Normalize(text), out translated!);
    }

    public bool Store(string text, string translated)
    {
        lock (gate)
        {
            text = TextRules.Normalize(text);
            // A human correction or an existing entry always has precedence over an in-flight request.
            if (entries.ContainsKey(text)) return false;
            try
            {
                var line = TextRules.Escape(text) + "\t" + TextRules.Escape(translated) + "\n";
                File.AppendAllText(Path, line, new UTF8Encoding(false));
                entries[text] = translated;
                sourceAmount += TextAmount.Measure(text); portugueseAmount += TextAmount.Measure(translated);
                Warning = null;
                if (File.Exists(BackupPath)) File.AppendAllText(BackupPath, line, new UTF8Encoding(false));
                else File.Copy(Path, BackupPath);
            }
            catch (IOException) { Warning = "Falha ao salvar a tradução ou sua cópia de recuperação. A preparação não concluirá entradas sem gravação no TXT."; }
            catch (UnauthorizedAccessException) { Warning = "Sem permissão para salvar as traduções ou sua cópia de recuperação."; }
            return entries.ContainsKey(text);
        }
    }

    public void Backup()
    {
        lock (gate)
        {
            var temporary = BackupPath + "." + Guid.NewGuid() + ".tmp";
            try { File.Copy(Path, temporary); File.Move(temporary, BackupPath, true); }
            catch (IOException) { Warning = "Não foi possível atualizar a cópia de recuperação do TXT."; }
            catch (UnauthorizedAccessException) { Warning = "Sem permissão para atualizar a cópia de recuperação do TXT."; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
