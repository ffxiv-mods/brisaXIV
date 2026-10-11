using System.Text.Json;
using System.Diagnostics;

namespace BrisaXIV.Core;

// Disk-confirmed progress, not just a list of requests dispatched to a provider.
public sealed class BackgroundPreparation
{
    private sealed class State
    {
        public string Fingerprint { get; set; } = "";
        public int Cursor { get; set; }
        public int SavedCount { get; set; }
        public bool Completed { get; set; }
    }
    private State state;
    private readonly string path;
    private readonly TranslationFile file;
    private readonly PreparationCatalog catalog;
    private int scheduledCursor;
    private int scheduledCharacters;
    private long nextPump;
    private long nextCheckpoint;
    private bool checkpointPending;
    private bool initialized;
    private bool halted;
    public CatalogVerification Verification { get; }
    public bool Completed => initialized && state.Completed && Verification.Ready && Verification.MissingCount == 0;
    public int Cursor => state.Cursor;
    public long ConfirmedCharacters => initialized ? catalog.ConfirmedCharacters(state.Cursor) : 0;
    public bool Initialized => initialized;
    public int SessionCharacters => scheduledCharacters;
    public string Message { get; private set; } = "Preparando o catálogo completo de textos japoneses…";
    public string CurrentGroup { get; private set; } = "";
    public int CurrentExpansion { get; private set; } = -1;

    public BackgroundPreparation(PreparationCatalog catalog, TranslationFile file, string path)
    {
        this.catalog = catalog; this.file = file; this.path = path;
        Verification = new(catalog, file);
        try { state = File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new() : new(); }
        catch (IOException) { state = new(); }
        catch (JsonException) { state = new(); }
    }

    public void Resume() { halted = false; scheduledCursor = state.Cursor; scheduledCharacters = 0; nextPump = 0; }

    public void Pump(TranslationQueue queue, bool enabled, int characterBudget, bool priorityActive = false, bool continuous = false)
    {
        if (!catalog.Ready) return;
        Verification.Pump();
        if (Environment.TickCount64 < nextPump) return;
        nextPump = Environment.TickCount64 + (queue.PreparationWindow > 24 ? 250 : 1000);
        if (!initialized)
        {
            if (state.Fingerprint != catalog.Fingerprint || state.Cursor > catalog.Ordered.Count || file.Count < state.SavedCount)
                state = new State { Fingerprint = catalog.Fingerprint };
            scheduledCursor = state.Cursor;
            state.Completed = false;
            initialized = true;
        }
        if (Completed) { Message = "Preparação concluída. Modo de leitura do TXT: nenhuma tradução automática é solicitada."; return; }
        if (Verification.Ready && Verification.MissingCount == 0)
        {
            state.Cursor = catalog.Ordered.Count; file.Backup(); state.Completed = true;
            if (!Save()) state.Completed = false;
            else Message = "Todos os textos do catálogo foram conferidos no TXT. Modo de leitura ativado.";
            return;
        }
        if (queue.PausedReason is { } paused) { Message = paused; return; }
        if (priorityActive) { Message = "Priorizando o texto aberto na HUD. A preparação geral retoma após a fala."; return; }
        if (!enabled) { Message = "Preparação pausada. O progresso permanece salvo."; return; }
        if (halted) return;
        var scanStarted = Stopwatch.GetTimestamp();
        var scanningLimited = false;
        // Cached lookups have their own frame budget, independent of API batch size.
        // Resume on the next frame instead of waiting a provider-specific second.
        for (var n = 0; state.Cursor < catalog.Ordered.Count; n++)
        {
            if (n >= 8192 || Stopwatch.GetElapsedTime(scanStarted).TotalMilliseconds >= 2)
            { scanningLimited = true; break; }
            if (!file.TryGet(catalog.Ordered[state.Cursor].Text, out _)) break;
            state.Cursor++; checkpointPending = true;
        }
        if (checkpointPending && Environment.TickCount64 >= nextCheckpoint)
        {
            if (!Save()) return;
            checkpointPending = false;
            nextCheckpoint = Environment.TickCount64 + 1000;
        }
        var current = catalog.Ordered[Math.Min(scheduledCursor, catalog.Ordered.Count - 1)];
        CurrentGroup = current.Group; CurrentExpansion = current.Expansion;
        scheduledCursor = Math.Max(scheduledCursor, state.Cursor);
        for (var n = 0; scheduledCursor < catalog.Ordered.Count && queue.PendingCount < queue.PreparationWindow; n++)
        {
            if (n >= 8192 || Stopwatch.GetElapsedTime(scanStarted).TotalMilliseconds >= 2)
            { scanningLimited = true; break; }
            if (!Schedule(queue, scheduledCursor, characterBudget, continuous)) return;
            scheduledCursor++;
        }
        if (scanningLimited) nextPump = Environment.TickCount64 + 16;
        // An audit can find a missing original before the old cursor even when the TXT
        // count stayed unchanged (e.g. an edited/imported file). Repair all such holes.
        if (scheduledCursor >= catalog.Ordered.Count && queue.PendingCount < queue.PreparationWindow)
            foreach (var index in Verification.MissingIndices(queue.PreparationWindow))
            {
                if (queue.PendingCount >= queue.PreparationWindow || !Schedule(queue, index, characterBudget, continuous)) break;
            }
        Message = $"Preparando em segundo plano. {scheduledCharacters:N0} caracteres agendados nesta sessão. "
            + (Verification.Ready ? $"{catalog.Ordered.Count - Verification.MissingCount:N0}/{catalog.Ordered.Count:N0} textos confirmados na conferência. " : $"Conferindo o TXT: {Verification.Checked:N0}/{catalog.Ordered.Count:N0}. ")
            + (queue.DeferredCount > 0 ? $"{queue.DeferredCount:N0} pendência(s) com nova tentativa automática, sem parar os demais textos." : "");
    }

    private bool Schedule(TranslationQueue queue, int index, int budget, bool continuous)
    {
        var entry = catalog.Ordered[index];
        if (file.TryGet(entry.Text, out _) || queue.IsDeferred(entry.Text) || queue.Get(entry.Text, false).Pending) return true;
        var characters = (int)TextAmount.Measure(entry.Text).Characters;
        if (!continuous && scheduledCharacters > 0 && scheduledCharacters + characters > Math.Max(1000, budget))
        { Message = "Parte desta sessão concluída. Ative Preparação contínua ou use Retomar mais uma parte; pendências já registradas têm nova tentativa automática."; return false; }
        var result = queue.Get(entry.Text, group: entry.Group, expansion: entry.Expansion);
        if (result.Error != null && !result.Pending) { Message = result.Error; return false; }
        scheduledCharacters += characters;
        CurrentGroup = entry.Group; CurrentExpansion = entry.Expansion;
        return true;
    }

    private bool Save()
    {
        state.SavedCount = file.Count;
        var temporary = path + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(state)); File.Move(temporary, path, true); return true; }
        catch (IOException) { halted = true; Message = "Falha ao salvar o progresso. Os textos já gravados continuam no TXT."; return false; }
        catch (UnauthorizedAccessException) { halted = true; Message = "Sem permissão para salvar o progresso. Confira as permissões da pasta."; return false; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
