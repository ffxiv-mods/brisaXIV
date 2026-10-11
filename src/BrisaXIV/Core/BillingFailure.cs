namespace BrisaXIV.Core;

public static class BillingFailure
{
    // Keep billing failures distinct from 429 request/token rate limiting.
    // Fixed messages never include the service's response body or credentials.
    public static string? Message(string? code) => code switch
    {
        "credit_balance_exhausted" => "A OpenAI informou que os créditos pré-pagos acabaram. Confira o saldo da API antes de retomar. O TXT e o progresso foram preservados.",
        "organization_spend_limit_exceeded" or "project_spend_limit_exceeded" or "billing_hard_limit_reached"
            => "A OpenAI informou que o limite de gastos da conta ou projeto foi atingido. Confira esse limite antes de retomar. O TXT e o progresso foram preservados.",
        "organization_usage_limit_exceeded" => "A OpenAI informou que a cota de uso da organização foi atingida. Confira a cota antes de retomar. O TXT e o progresso foram preservados.",
        "insufficient_quota" or "billing_not_active" => "A OpenAI informou falta de saldo, cota ou faturamento ativo. Confira a conta da API antes de retomar. O TXT e o progresso foram preservados.",
        _ => null,
    };
}
