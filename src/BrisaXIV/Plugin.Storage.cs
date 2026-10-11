using BrisaXIV.Core;

namespace BrisaXIV;

public sealed partial class Plugin
{
    private void ImportPackageTranslations(string packageRoot, string dataRoot)
    {
        try
        {
            Directory.CreateDirectory(dataRoot);
            var destination = Path.Combine(dataRoot, "traducoes.txt");
            var seed = Path.Combine(packageRoot, "traducoes.txt");
            // First install only: never overwrite personal translations on updates.
            if (!File.Exists(destination) && !File.Exists(destination + ".bak") && File.Exists(seed))
                File.Copy(seed, destination, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { uiError = "Não foi possível importar o TXT inicial. Confira as permissões da pasta de dados do plugin."; }
    }

    // Each update ships the newest published TXT. Only originals the player does not have yet are
    // added, in the background and once per package, so personal corrections always survive.
    private void MergePackageTranslations(string packageRoot, string dataRoot)
    {
        var seed = Path.Combine(packageRoot, "traducoes.txt");
        var marker = Path.Combine(dataRoot, "pacote-importado.txt");
        try
        {
            if (!File.Exists(seed)) return;
            var info = new FileInfo(seed);
            var signature = info.Length + " " + info.LastWriteTimeUtc.Ticks;
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == signature) return;
            _ = Task.Run(() =>
            {
                try
                {
                    var added = file.ImportMissing(seed);
                    File.WriteAllText(marker, signature);
                    Log.Information("BrisaXIV: {Count} traduções novas do pacote incorporadas ao TXT.", added);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Log.Warning("BrisaXIV: não foi possível incorporar as traduções novas do pacote ({ErrorType}).", ex.GetType().Name); }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Warning("BrisaXIV: não foi possível conferir o TXT do pacote ({ErrorType}).", ex.GetType().Name); }
    }
}
