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

    // ── Anmeldung ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der angemeldete Benutzer, oder <c>null</c>, wenn keine gültige Sitzung besteht.
    /// Ein 401 ist hier kein Fehler, sondern die Antwort „nicht angemeldet“.
    /// </summary>
    public async Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync("api/auth/me", ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return null;
            }

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<CurrentUserDto>(Json, ct)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ApiException(Describe(ex), ex);
        }
    }

    public Task<CurrentUserDto> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => PostAsync<LoginRequest, CurrentUserDto>("api/auth/login", request, ct);

    public Task<CurrentUserDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        => PostAsync<RegisterRequest, CurrentUserDto>("api/auth/register", request, ct);

    public Task LogoutAsync(CancellationToken ct = default)
        => SendWithoutResultAsync(HttpMethod.Post, "api/auth/logout", (object?)null, ct);

    public Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
        => SendWithoutResultAsync(
            HttpMethod.Post, "api/auth/password-reset", new PasswordResetStartRequest { Email = email }, ct);

    public Task RedeemPasswordResetAsync(string token, string newPassword, CancellationToken ct = default)
        => SendWithoutResultAsync(
            HttpMethod.Post,
            "api/auth/password-reset/redeem",
            new PasswordResetRedeemRequest { Token = token, NewPassword = newPassword },
            ct);

    public Task<HouseholdOverviewDto> GetHouseholdAsync(CancellationToken ct = default)
        => GetAsync<HouseholdOverviewDto>("api/household", ct);

    public Task<InvitationDto> CreateInvitationAsync(CancellationToken ct = default)
        => PostAsync<object?, InvitationDto>("api/household/invitations", null, ct);

    // ── Fachdaten ──────────────────────────────────────────────────────────────────────────

    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
        => GetAsync<DashboardDto>("api/dashboard", ct);

    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<AccountDto>>("api/accounts", ct);

    /// <summary>Buchungsliste mit Suche und Filtern. Leere Filter bleiben aus der Adresse weg.</summary>
    public Task<TransactionPageDto> GetTransactionsAsync(
        string? search = null,
        int? accountId = null,
        int? categoryId = null,
        TransactionKind? kind = null,
        bool uncategorizedOnly = false,
        CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add("search=" + Uri.EscapeDataString(search));
        }

        if (accountId is { } account)
        {
            parts.Add("account=" + account);
        }

        if (categoryId is { } category)
        {
            parts.Add("category=" + category);
        }

        if (kind is { } wanted)
        {
            parts.Add("kind=" + wanted);
        }

        if (uncategorizedOnly)
        {
            parts.Add("offen=true");
        }

        var url = parts.Count == 0 ? "api/transactions" : "api/transactions?" + string.Join("&", parts);
        return GetAsync<TransactionPageDto>(url, ct);
    }

    /// <summary>Stapelvergabe für mehrere Buchungen auf einmal.</summary>
    public Task<BatchAssignResultDto> AssignCategoryBatchAsync(
        BatchAssignRequest request, CancellationToken ct = default)
        => PostAsync<BatchAssignRequest, BatchAssignResultDto>("api/transactions/batch-category", request, ct);

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

    internal async Task<T> GetAsync<T>(string url, CancellationToken ct)
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

    internal Task<TResult> PostAsync<TBody, TResult>(string url, TBody body, CancellationToken ct)
        => SendAsync<TBody, TResult>(HttpMethod.Post, url, body, ct);

    /// <summary>Für Endpunkte, die 204 antworten.</summary>
    internal async Task SendWithoutResultAsync<TBody>(
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
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ApiException(Describe(ex), ex);
        }
    }

    internal async Task<TResult> SendAsync<TBody, TResult>(
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

    /// <summary>
    /// Schickt ein Formular mit Datei. Der Schutz gegen fremde Formulare liegt beim Anmelde-Cookie
    /// mit SameSite=Strict — ein anderer Ursprung bekommt es nicht mitgeschickt.
    /// </summary>
    internal async Task<TResult> PostFormAsync<TResult>(
        string url, MultipartFormDataContent form, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsync(url, form, ct);
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

    /// <summary>
    /// Die Meldung des Servers hat Vorrang — bei der Anmeldung ist sie bewusst formuliert und
    /// darf nicht durch einen allgemeinen Text ersetzt werden.
    /// </summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(Json, ct);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Keine Problem-Details im Rumpf — dann greift die Meldung unten.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Nicht angemeldet.",
            HttpStatusCode.Forbidden => "Dafür fehlen die Rechte.",
            HttpStatusCode.NotFound => "Der Datensatz wurde nicht gefunden.",
            HttpStatusCode.TooManyRequests => "Zu viele Versuche. Bitte kurz warten.",
            _ => "Der Server hat die Anfrage abgelehnt.",
        };
    }

    private sealed record ProblemPayload(string? Title, string? Detail);
}
