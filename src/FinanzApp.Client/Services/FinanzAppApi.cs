using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Client.Services;

/// <summary>Fehler beim Zugriff auf die API, aufbereitet für die Oberfläche.</summary>
public sealed class ApiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Zugriff auf die Anwendungsschicht. Die einzige Stelle im Client, die HTTP kennt —
/// Komponenten arbeiten mit den Verträgen aus <c>FinanzApp.Shared</c>.
/// </summary>
public sealed class FinanzAppApi(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
        => GetAsync<DashboardDto>("api/dashboard", ct);

    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<AccountDto>>("api/accounts", ct);

    public Task<TransactionPageDto> GetTransactionsAsync(string? search = null, CancellationToken ct = default)
        => GetAsync<TransactionPageDto>(
            string.IsNullOrWhiteSpace(search)
                ? "api/transactions"
                : "api/transactions?search=" + Uri.EscapeDataString(search),
            ct);

    public Task<TransactionDto> CreateTransactionAsync(
        CreateTransactionRequest request, CancellationToken ct = default)
        => PostAsync<CreateTransactionRequest, TransactionDto>("api/transactions", request, ct);

    public Task<TransactionDto> AssignCategoryAsync(
        int transactionId, AssignCategoryRequest request, CancellationToken ct = default)
        => SendAsync<AssignCategoryRequest, TransactionDto>(
            HttpMethod.Patch, $"api/transactions/{transactionId}/category", request, ct);

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(
        CategoryDirection? direction = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<CategoryDto>>(
            direction is null ? "api/categories" : "api/categories?direction=" + direction, ct);

    public Task<BudgetOverviewDto> GetBudgetsAsync(BudgetPeriod period, CancellationToken ct = default)
        => GetAsync<BudgetOverviewDto>("api/budgets?period=" + period, ct);

    public Task<PortfolioDto> GetPortfolioAsync(CancellationToken ct = default)
        => GetAsync<PortfolioDto>("api/portfolio", ct);

    public Task<LoanDto> GetLoanAsync(int id, CancellationToken ct = default)
        => GetAsync<LoanDto>($"api/loans/{id}", ct);

    /// <summary>Das erste Darlehen — der Einstieg, wenn kein bestimmtes verlangt wurde.</summary>
    public Task<LoanDto> GetPrimaryLoanAsync(CancellationToken ct = default)
        => GetAsync<LoanDto>("api/loans/primary", ct);

    public Task<ImportPreviewDto> GetImportPreviewAsync(CancellationToken ct = default)
        => GetAsync<ImportPreviewDto>("api/import/preview", ct);

    public Task<ImportCommitResultDto> CommitImportAsync(Guid previewId, CancellationToken ct = default)
        => PostAsync<object?, ImportCommitResultDto>($"api/import/{previewId}/commit", null, ct);

    public Task<MoreOverviewDto> GetMoreOverviewAsync(CancellationToken ct = default)
        => GetAsync<MoreOverviewDto>("api/overview/more", ct);

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<T>(url, Json, ct)
                   ?? throw new ApiException("Die Antwort des Servers war leer.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ApiException(Describe(ex), ex);
        }
    }

    private Task<TResult> PostAsync<TBody, TResult>(string url, TBody body, CancellationToken ct)
        => SendAsync<TBody, TResult>(HttpMethod.Post, url, body, ct);

    private async Task<TResult> SendAsync<TBody, TResult>(
        HttpMethod method, string url, TBody body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException(await DescribeAsync(response, ct));
            }

            return await response.Content.ReadFromJsonAsync<TResult>(Json, ct)
                   ?? throw new ApiException("Die Antwort des Servers war leer.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ApiException(Describe(ex), ex);
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "Der Server hat nicht rechtzeitig geantwortet.",
        HttpRequestException => "Keine Verbindung zum Server.",
        _ => "Die Antwort des Servers war unlesbar.",
    };

    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(Json, ct);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => "Der Datensatz wurde nicht gefunden.",
            _ => "Der Server hat die Anfrage abgelehnt.",
        };
    }

    private sealed record ProblemPayload(string? Title, string? Detail);
}
