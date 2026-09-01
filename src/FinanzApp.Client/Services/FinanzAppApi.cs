using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>Die Gemeinschaftskonten samt Einzahlungssoll je Beteiligtem.</summary>
    public Task<IReadOnlyList<JointAccountDto>> GetJointAccountsAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<JointAccountDto>>("api/accounts/gemeinschaft", ct);

    /// <summary>Ändert die Freigabe eines Kontos. Nur der Eigentümer darf das.</summary>
    public Task<AccountDto> SetAccountSharingAsync(
        int id, AccountSharing sharing, IReadOnlyList<int> userIds, CancellationToken ct = default)
        => SendAsync<AccountSharingRequest, AccountDto>(
            HttpMethod.Put, $"api/accounts/{id}/sharing", new AccountSharingRequest(sharing, userIds), ct);

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

    /// <summary>
    /// Löscht eine Buchung. Über den 204-Helfer — der Endpunkt schickt keinen Rumpf, und ein
    /// JSON-Leser bräche daran.
    /// </summary>
    public Task DeleteTransactionAsync(int id, CancellationToken ct = default)
        => SendWithoutResultAsync<object?>(HttpMethod.Delete, $"api/transactions/{id}", null, ct);

    /// <summary>Löscht mehrere Buchungen und liefert, wie viele es waren.</summary>
    public Task<int> DeleteTransactionsAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
        => PostAsync<BatchAssignRequest, int>(
            "api/transactions/batch-delete", new BatchAssignRequest { TransactionIds = ids }, ct);

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

    public Task<BudgetOverviewDto> GetBudgetsAsync(PeriodScope period, CancellationToken ct = default)
        => GetAsync<BudgetOverviewDto>("api/budgets?period=" + period, ct);

    /// <summary>Das Depot. <c>null</c>, wenn der Haushalt keines führt.</summary>
    public Task<PortfolioDto?> GetPortfolioAsync(CancellationToken ct = default)
        => GetOrNullAsync<PortfolioDto>("api/portfolio", ct);

    public Task<LoanDto> GetLoanAsync(int id, CancellationToken ct = default)
        => GetAsync<LoanDto>($"api/loans/{id}", ct);

    /// <summary>
    /// Das erste Darlehen — der Einstieg, wenn kein bestimmtes verlangt wurde.
    /// <c>null</c>, wenn der Haushalt keines führt.
    /// </summary>
    public Task<LoanDto?> GetPrimaryLoanAsync(CancellationToken ct = default)
        => GetOrNullAsync<LoanDto>("api/loans/primary", ct);

    public Task<ImportPreviewDto> GetImportPreviewAsync(CancellationToken ct = default)
        => GetAsync<ImportPreviewDto>("api/import/preview", ct);

    /// <summary>Liest eine hochgeladene Auszugsdatei und liefert die Vorschau dazu.</summary>
    public async Task<ImportPreviewDto> ReadStatementAsync(
        Stream content, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        return await PostFormAsync<ImportPreviewDto>("api/import/read", form, ct);
    }

    /// <summary>Übernimmt die gewählten Sätze auf das gewählte Konto, samt Zuordnungen.</summary>
    public Task<ImportCommitResultDto> CommitImportAsync(
        Guid previewId,
        int accountId,
        IReadOnlyList<int> indexes,
        IReadOnlyList<ImportCategoryChoice>? choices = null,
        ImportKeepFields? keep = null,
        IReadOnlyList<ImportKeepOverride>? keepOverrides = null,
        CancellationToken ct = default)
        => PostAsync<ImportCommitRequest, ImportCommitResultDto>(
            "api/import/commit",
            new ImportCommitRequest
            {
                PreviewId = previewId,
                AccountId = accountId,
                Indexes = indexes,
                Choices = choices ?? [],
                Keep = keep ?? new ImportKeepFields(),
                KeepOverrides = keepOverrides ?? [],
            },
            ct);

    /// <summary>Löscht eine gelernte Kategorieregel.</summary>
    public Task DeleteRuleAsync(int id, CancellationToken ct = default)
        => SendWithoutResultAsync<object?>(HttpMethod.Delete, $"api/rules/{id}", null, ct);

    /// <summary>Die Kategorien einer Richtung samt Verwendungsnachweis.</summary>
    public Task<IReadOnlyList<CategoryUsageDto>> GetCategoryUsageAsync(
        CategoryDirection direction, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<CategoryUsageDto>>("api/categories/usage?direction=" + direction, ct);

    public Task<CategoryDto> CreateCategoryAsync(
        string name, CategoryDirection direction, CancellationToken ct = default)
        => PostAsync<CategoryNameRequest, CategoryDto>(
            "api/categories", new CategoryNameRequest { Name = name, Direction = direction }, ct);

    /// <summary>Legt eine Kategorie an oder liefert die, die den Namen schon trägt.</summary>
    public Task<CategoryEnsureResultDto> EnsureCategoryAsync(
        string name, CategoryDirection direction, CancellationToken ct = default)
        => PostAsync<CategoryNameRequest, CategoryEnsureResultDto>(
            "api/categories/ensure", new CategoryNameRequest { Name = name, Direction = direction }, ct);

    public Task<CategoryChangeResultDto> RenameCategoryAsync(
        int id, string name, CancellationToken ct = default)
        => SendAsync<CategoryNameRequest, CategoryChangeResultDto>(
            HttpMethod.Patch, $"api/categories/{id}", new CategoryNameRequest { Name = name }, ct);

    public Task<CategoryChangeResultDto> DeleteCategoryAsync(int id, CancellationToken ct = default)
        => SendAsync<object?, CategoryChangeResultDto>(
            HttpMethod.Delete, $"api/categories/{id}", null, ct);

    /// <summary>
    /// Der Kostentrend. Alles Rechnen liegt beim Server — auch nach jedem Aus- und Zuschalten
    /// einer Buchung, damit es nur eine Rechnung gibt.
    /// </summary>
    public Task<CostTrendDto> GetCostTrendAsync(
        CostTrendRequest request, CancellationToken ct = default)
        => PostAsync<CostTrendRequest, CostTrendDto>("api/reports/cost-trend", request, ct);

    /// <summary>Fixkosten und vertragliche Bindung — auf derselben Monatsbasis.</summary>
    public Task<FixedCostsDto> GetFixedCostsAsync(
        FixedCostsRequest request, CancellationToken ct = default)
        => PostAsync<FixedCostsRequest, FixedCostsDto>("api/reports/fixed-costs", request, ct);

    /// <summary>Gewinn und Verlust eines Depots. <c>null</c>, wenn keines erfasst ist.</summary>
    public async Task<PortfolioGainDto?> GetPortfolioGainAsync(
        int? depotId = null, CancellationToken ct = default)
    {
        var url = depotId is { } id
            ? $"api/reports/portfolio-gain?depot={id}"
            : "api/reports/portfolio-gain";

        // Nur die 404 wird zu null. Vorher fing der Aufruf jede ApiException ab — ein Serverfehler
        // sah dann aus wie „kein Depot erfasst“, und der Bericht schwieg über einen Ausfall.
        return await GetOrNullAsync<PortfolioGainDto>(url, ct);
    }

    /// <summary>
    /// Objekt &amp; Beteiligung. <c>null</c>, wenn keine Immobilie erfasst ist.
    /// </summary>
    public async Task<PropertyReportDto?> GetPropertyReportAsync(
        int? propertyId = null, CancellationToken ct = default)
    {
        var url = propertyId is { } id
            ? $"api/reports/objekt?objekt={id}"
            : "api/reports/objekt";

        return await GetOrNullAsync<PropertyReportDto>(url, ct);
    }

    /// <summary>Die gespeicherten Ansichten des Auswertungsbereichs.</summary>
    public Task<IReadOnlyList<ReportViewDto>> GetReportViewsAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<ReportViewDto>>("api/reports/views", ct);

    /// <summary>Legt eine Ansicht ab. Ein doppelter Name kommt als <see cref="ApiException"/>.</summary>
    public Task<ReportViewDto> SaveReportViewAsync(
        SaveReportViewRequest request, CancellationToken ct = default)
        => PostAsync<SaveReportViewRequest, ReportViewDto>("api/reports/views", request, ct);

    public Task DeleteReportViewAsync(int id, CancellationToken ct = default)
        => SendWithoutResultAsync<object?>(HttpMethod.Delete, $"api/reports/views/{id}", null, ct);

    /// <summary>Der Bestand — alle Objekte in einer Liste, wahlweise nach Klasse gefiltert.</summary>
    public Task<HoldingsDto> GetHoldingsAsync(
        HoldingClass? klasse = null, CancellationToken ct = default)
        => GetAsync<HoldingsDto>(
            klasse is { } k ? $"api/holdings?klasse={k}" : "api/holdings", ct);

    /// <summary>Die PKV-Bilanz eines Kalenderjahres.</summary>
    public Task<HealthBalanceDto> GetHealthBalanceAsync(
        int? jahr = null, bool alle = false, CancellationToken ct = default)
        => GetAsync<HealthBalanceDto>(
            alle
                ? "api/reports/health-balance?alle=true"
                : jahr is { } j
                    ? $"api/reports/health-balance?jahr={j}"
                    : "api/reports/health-balance",
            ct);

    /// <summary>Was die Auswertungen unvollständig macht.</summary>
    public Task<DataQualityDto> GetDataQualityAsync(CancellationToken ct = default)
        => GetAsync<DataQualityDto>("api/reports/data-quality", ct);

    /// <summary>Die gepflegten Dokumenttypen samt Verwendungsnachweis.</summary>
    public Task<DocumentTypeOverviewDto> GetDocumentTypesAsync(CancellationToken ct = default)
        => GetAsync<DocumentTypeOverviewDto>("api/document-types", ct);

    public Task<DocumentTypeUsageDto> CreateDocumentTypeAsync(
        string name, DocumentArea area, CancellationToken ct = default)
        => PostAsync<DocumentTypeNameRequest, DocumentTypeUsageDto>(
            "api/document-types", new DocumentTypeNameRequest(name, area), ct);

    public Task<DocumentTypeChangeResultDto> RenameDocumentTypeAsync(
        int id, string name, CancellationToken ct = default)
        => SendAsync<DocumentTypeNameRequest, DocumentTypeChangeResultDto>(
            HttpMethod.Patch, $"api/document-types/{id}", new DocumentTypeNameRequest(name), ct);

    public Task<DocumentTypeChangeResultDto> DeleteDocumentTypeAsync(
        int id, CancellationToken ct = default)
        => SendAsync<object?, DocumentTypeChangeResultDto>(
            HttpMethod.Delete, $"api/document-types/{id}", null, ct);

    /// <summary>Die gelernten Kategorieregeln.</summary>
    public Task<IReadOnlyList<CategorizationRuleDto>> GetRulesAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<CategorizationRuleDto>>("api/rules", ct);

    /// <summary>Ändert das eigene Passwort. Fehler kommen als <see cref="ApiException"/>.</summary>
    public Task ChangePasswordAsync(
        string currentPassword, string newPassword, CancellationToken ct = default)
        => SendWithoutResultAsync(
            HttpMethod.Post, "api/auth/password",
            new ChangePasswordRequest { CurrentPassword = currentPassword, NewPassword = newPassword },
            ct);

    public Task<MoreOverviewDto> GetMoreOverviewAsync(CancellationToken ct = default)
        => GetAsync<MoreOverviewDto>("api/overview/more", ct);

    internal async Task<T> GetAsync<T>(string url, CancellationToken ct)
        => await GetOrNullAsync<T>(url, nullOnNotFound: false, ct)
           ?? throw new ApiException("Die Antwort des Servers war leer.");

    /// <summary>
    /// Wie <see cref="GetAsync{T}"/>, gibt aber bei 404 <c>null</c> zurück statt zu werfen.
    /// </summary>
    /// <remarks>
    /// Für Bereiche, die es im Haushalt schlicht nicht gibt — kein Depot, kein Darlehen. Das ist
    /// kein Fehler, sondern eine Auskunft, und die Seite macht daraus einen Leerzustand.
    /// </remarks>
    internal Task<T?> GetOrNullAsync<T>(string url, CancellationToken ct)
        => GetOrNullAsync<T>(url, nullOnNotFound: true, ct);

    /// <summary>
    /// Ein GET, das die Antwort des Servers wirklich liest.
    /// </summary>
    /// <remarks>
    /// Vorher lief das über <c>GetFromJsonAsync</c>. Das wirft bei <em>jedem</em> Status außerhalb
    /// von 2xx eine <see cref="HttpRequestException"/> — und die wurde hier zu „Keine Verbindung
    /// zum Server“. Ein Haushalt ohne Depot bekam damit unter /depot eine Meldung über das Netz,
    /// obwohl der Server sauber mit 404 geantwortet hatte. Der Rumpf mit den Problemdetails ging
    /// dabei ebenfalls verloren, also auch jede Meldung, die der Server sich überlegt hatte.
    /// </remarks>
    private async Task<T?> GetOrNullAsync<T>(string url, bool nullOnNotFound, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);

            if (nullOnNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException(await DescribeAsync(response, ct));
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
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

        // Nur ohne Status ist es wirklich das Netz. Trägt die Ausnahme einen, hat der Server
        // geantwortet — dann wäre „keine Verbindung“ eine Behauptung über eine Ursache, die
        // gerade widerlegt vorliegt.
        HttpRequestException { StatusCode: not null } => "Der Server hat die Anfrage abgelehnt.",
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
