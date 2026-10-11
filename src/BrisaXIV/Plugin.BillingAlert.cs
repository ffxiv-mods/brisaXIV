using System.Numerics;
using BrisaXIV.Core;
using Dalamud.Bindings.ImGui;

namespace BrisaXIV;

public sealed partial class Plugin
{
    private string? shownBillingFailure;
    private bool billingAlertOpen;
    private void ShareBillingPause()
    {
        var code = queue.BillingFailureCode ?? clientQueue.BillingFailureCode;
        if (code == null || BillingFailure.Message(code) is not { } message) return;
        queue.PauseForBilling(code, message); clientQueue.PauseForBilling(code, message);
    }
    private void DrawBillingAlert()
    {
        var code = queue.BillingFailureCode ?? clientQueue.BillingFailureCode;
        if (code == null) { shownBillingFailure = null; return; }
        PushTheme();
        try
        {
            if (shownBillingFailure != code)
            { shownBillingFailure = code; billingAlertOpen = true; ImGui.OpenPopup("Saldo ou cota da IA###BrisaBillingAlert"); }
            ImGui.SetNextWindowSize(new Vector2(510, 0), ImGuiCond.Appearing);
            if (ImGui.BeginPopupModal("Saldo ou cota da IA###BrisaBillingAlert", ref billingAlertOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextColored(Amber, code == "credit_balance_exhausted" ? "Créditos da API esgotados" : "Tradução com IA pausada");
                ImGui.TextWrapped(BillingFailure.Message(code) ?? "Confira o saldo e as cotas da API.");
                ImGui.TextWrapped("Traduções que já estão no TXT continuam disponíveis. Fechar este alerta mantém os novos envios pausados. Após regularizar a conta, use Tentar novamente após erro.");
                if (ImGui.Button("Entendi")) ImGui.CloseCurrentPopup();
                ImGui.SameLine();
                if (ImGui.Button("Ver configurações"))
                { settingsOpen = true; dashboardPage = DashboardPage.Translator; ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
        }
        finally { PopTheme(); }
    }
}
