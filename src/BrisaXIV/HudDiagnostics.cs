using System.Text;
using BrisaXIV.Core;

namespace BrisaXIV;

// Opt-in trace (/brisa diagnostico) of what the HUD capture reads and writes in the journal, action and item
// tooltip windows. Only game text is recorded, one line per change of a text node's state, so a
// reproduction stays small. Runs on the framework thread only.
internal sealed class HudDiagnostics(string path)
{
    private const long MaximumSize = 8 * 1024 * 1024;
    private readonly Dictionary<(nint Address, string Kind), string> last = new();
    private bool full;
    public string Path { get; } = path;
    public bool Enabled { get; private set; }

    public bool Watches(string addon) => Enabled && !full && (addon.StartsWith("Journal", StringComparison.Ordinal) || addon is "ActionDetail" or "ItemDetail");

    public void Toggle()
    {
        Enabled = !Enabled;
        last.Clear(); full = false;
        Write(Enabled ? "=== diagnóstico iniciado" : "=== diagnóstico encerrado", force: true);
    }

    public void Record(nint address, string kind, string addon, string details)
    {
        if (last.TryGetValue((address, kind), out var previous) && previous == details) return;
        last[(address, kind)] = details;
        Write($"{addon} node=0x{address:X} {kind} {details}");
    }

    public static string Quote(string? text) => text == null ? "-" : "\"" + TextRules.Escape(text) + "\"";

    private void Write(string line, bool force = false)
    {
        if (full && !force) return;
        try
        {
            if (File.Exists(Path) && new FileInfo(Path).Length > MaximumSize)
            { full = true; File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + " === limite de 8 MiB atingido; diagnóstico pausado\n", new UTF8Encoding(false)); return; }
            File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\n", new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
