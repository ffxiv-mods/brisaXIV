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
}
