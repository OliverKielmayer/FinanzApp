using System.Net.Http.Headers;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Client.Services;

/// <summary>
/// Die Aufrufe der Erweiterung — Dokumente, Vorgänge, Gesundheit, Versicherungen, Wohnen,
/// Liquidität. Eigene Datei, damit der Bestand von <see cref="FinanzAppApi"/> übersichtlich bleibt.
/// </summary>
public static class FinanzAppApiExtensions
{
    // ── Dokumente ──────────────────────────────────────────────────────────────────────────

    public static Task<DocumentPageDto> GetDocumentsAsync(
        this FinanzAppApi api, DocumentArea? area = null, string? search = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (area is { } value)
        {
            query.Add("area=" + value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add("search=" + Uri.EscapeDataString(search));
        }

        return api.GetAsync<DocumentPageDto>(
            "api/documents" + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query)), ct);
    }

    public static Task<IReadOnlyList<DocumentTypeDto>> GetDocumentTypesAsync(
        this FinanzAppApi api, CancellationToken ct = default)
        => api.GetAsync<IReadOnlyList<DocumentTypeDto>>("api/documents/types", ct);

    public static Task<DocumentSearchResultDto> SearchDocumentsAsync(
        this FinanzAppApi api, string term, CancellationToken ct = default)
        => api.GetAsync<DocumentSearchResultDto>("api/documents/search?q=" + Uri.EscapeDataString(term), ct);

    public static Task<DocumentDetailDto> GetDocumentAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<DocumentDetailDto>($"api/documents/{id}", ct);

    public static Task<DocumentDetailDto> UpdateDocumentAsync(
        this FinanzAppApi api, int id, UpdateDocumentRequest request, CancellationToken ct = default)
        => api.SendAsync<UpdateDocumentRequest, DocumentDetailDto>(
            HttpMethod.Put, $"api/documents/{id}", request, ct);

    public static Task<DocumentDetailDto> FixDocumentPathAsync(
        this FinanzAppApi api, int id, string relativePath, CancellationToken ct = default)
        => api.SendAsync<FixDocumentPathRequest, DocumentDetailDto>(
            HttpMethod.Put, $"api/documents/{id}/path", new FixDocumentPathRequest { RelativePath = relativePath }, ct);

    public static Task<DocumentLinkDto> LinkDocumentAsync(
        this FinanzAppApi api, int id, LinkTargetType type, int targetId, CancellationToken ct = default)
        => api.SendAsync<CreateDocumentLinkRequest, DocumentLinkDto>(
            HttpMethod.Post,
            $"api/documents/{id}/links",
            new CreateDocumentLinkRequest { TargetType = type, TargetId = targetId },
            ct);

    public static Task UnlinkDocumentAsync(this FinanzAppApi api, int linkId, CancellationToken ct = default)
        => api.SendWithoutResultAsync<object?>(HttpMethod.Delete, $"api/documents/links/{linkId}", null, ct);

    /// <summary>Lädt eine Datei hoch und legt dazu einen Dokumenteintrag an.</summary>
    public static async Task<DocumentUploadResultDto> UploadDocumentAsync(
        this FinanzAppApi api,
        Stream content,
        string fileName,
        DocumentArea area,
        string? title = null,
        int? documentTypeId = null,
        DateOnly? documentDate = null,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        var query = new List<string> { "area=" + area };
        if (!string.IsNullOrWhiteSpace(title))
        {
            query.Add("title=" + Uri.EscapeDataString(title));
        }

        if (documentTypeId is { } typeId)
        {
            query.Add("documentTypeId=" + typeId);
        }

        if (documentDate is { } date)
        {
            query.Add("documentDate=" + date.ToString("yyyy-MM-dd"));
        }

        return await api.PostFormAsync<DocumentUploadResultDto>(
            "api/documents?" + string.Join('&', query), form, ct);
    }

    // ── Vorgänge ───────────────────────────────────────────────────────────────────────────

    public static Task<TaskListDto> GetTasksAsync(
        this FinanzAppApi api, TaskState? state = null, CancellationToken ct = default)
        => api.GetAsync<TaskListDto>("api/tasks" + (state is { } s ? "?state=" + s : string.Empty), ct);

    public static Task<OpenWorkSummaryDto> GetWorkSummaryAsync(
        this FinanzAppApi api, CancellationToken ct = default)
        => api.GetAsync<OpenWorkSummaryDto>("api/tasks/summary", ct);

    public static Task<TaskItemDto> CreateTaskAsync(
        this FinanzAppApi api, CreateTaskRequest request, CancellationToken ct = default)
        => api.SendAsync<CreateTaskRequest, TaskItemDto>(HttpMethod.Post, "api/tasks", request, ct);

    public static Task SetTaskStateAsync(
        this FinanzAppApi api, int id, TaskState state, CancellationToken ct = default)
        => api.SendWithoutResultAsync(
            HttpMethod.Patch, $"api/tasks/{id}/state", new UpdateTaskStateRequest { State = state }, ct);

    // ── Gesundheit / PKV ───────────────────────────────────────────────────────────────────

    public static Task<IReadOnlyList<MedicalBillListItemDto>> GetMedicalBillsAsync(
        this FinanzAppApi api, CancellationToken ct = default)
        => api.GetAsync<IReadOnlyList<MedicalBillListItemDto>>("api/health/bills", ct);

    public static Task<MedicalBillDetailDto> GetMedicalBillAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<MedicalBillDetailDto>($"api/health/bills/{id}", ct);

    public static Task<MedicalBillDetailDto> CreateMedicalBillAsync(
        this FinanzAppApi api, CreateMedicalBillRequest request, CancellationToken ct = default)
        => api.SendAsync<CreateMedicalBillRequest, MedicalBillDetailDto>(
            HttpMethod.Post, "api/health/bills", request, ct);

    public static Task<MedicalBillDetailDto> AdvanceMedicalBillAsync(
        this FinanzAppApi api, int id, MedicalBillStatus status, CancellationToken ct = default)
        => api.SendAsync<AdvanceMedicalBillRequest, MedicalBillDetailDto>(
            HttpMethod.Patch,
            $"api/health/bills/{id}/status",
            new AdvanceMedicalBillRequest { Status = status },
            ct);

    public static Task<IReadOnlyList<PaymentCandidateDto>> GetBillPaymentCandidatesAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<IReadOnlyList<PaymentCandidateDto>>($"api/health/bills/{id}/payment-candidates", ct);

    public static Task<MedicalBillDetailDto> LinkBillPaymentAsync(
        this FinanzAppApi api, int id, int transactionId, CancellationToken ct = default)
        => api.SendAsync<LinkPaymentRequest, MedicalBillDetailDto>(
            HttpMethod.Post,
            $"api/health/bills/{id}/payment",
            new LinkPaymentRequest { TransactionId = transactionId },
            ct);

    /// <summary>Schickt einen Beleg zur Erkennung. Ohne angebundene Texterkennung kommt er leer zurück.</summary>
    public static async Task<ExtractedBillDto> ExtractBillAsync(
        this FinanzAppApi api, Stream content, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        return await api.PostFormAsync<ExtractedBillDto>("api/health/extract", form, ct);
    }

    // ── Vorsorge & Absicherung ────────────────────────────────────────

    /// <summary>Einer der beiden Bereiche — dasselbe Modell, andere Kopfzahl.</summary>
    public static Task<PolicyOverviewDto> GetPoliciesAsync(
        this FinanzAppApi api, bool capitalForming, CancellationToken ct = default)
        => api.GetAsync<PolicyOverviewDto>(
            capitalForming ? "api/policies/vorsorge" : "api/policies/absicherung", ct);

    public static Task<PolicyDetailDto> GetPolicyAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<PolicyDetailDto>($"api/policies/{id}", ct);

    // ── Anlegen ───────────────────────────────────────────────────────────

    /// <summary>Die Feldliste eines Objekttyps, samt Auswahlwerten aus dem Bestand.</summary>
    public static Task<CreateFormDto> GetCreateFormAsync(
        this FinanzAppApi api, CreateObjectType type, CancellationToken ct = default)
        => api.GetAsync<CreateFormDto>($"api/create/{type}", ct);

    /// <summary>
    /// Legt an. Ein Fehlschlag kommt als Ergebnis zurück, nicht als Ausnahme — die Oberfläche
    /// braucht das bemängelte Feld, nicht einen Statuscode.
    /// </summary>
    public static Task<CreateResultDto> CreateObjectAsync(
        this FinanzAppApi api, CreateObjectType type, Dictionary<string, string?> values,
        CancellationToken ct = default)
        => api.PostAsync<CreateRequest, CreateResultDto>(
            $"api/create/{type}", new CreateRequest { Values = values }, ct);

    /// <summary>
    /// Schickt eine Police zur Analyse. Die Datei wird in jedem Fall abgelegt; ohne angebundene
    /// Analyse kommt sie ohne erkannte Werte zurück.
    /// </summary>
    public static async Task<DocumentAnalysisDto> AnalysePolicyAsync(
        this FinanzAppApi api, CreateObjectType type, Stream content, string fileName,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        return await api.PostFormAsync<DocumentAnalysisDto>($"api/create/{type}/analyse", form, ct);
    }

    /// <summary>Vermerkt die gelesenen Werte als übernommen.</summary>
    public static Task<int> ConfirmExtractionsAsync(
        this FinanzAppApi api, int documentId, CancellationToken ct = default)
        => api.PostAsync<object?, int>($"api/create/extractions/{documentId}/confirm", null, ct);

    // ── Wohnen ─────────────────────────────────────────────────────────────────────────────

    public static Task<IReadOnlyList<PropertyListItemDto>> GetPropertiesAsync(
        this FinanzAppApi api, CancellationToken ct = default)
        => api.GetAsync<IReadOnlyList<PropertyListItemDto>>("api/properties", ct);

    public static Task<PropertyDetailDto> GetPropertyAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<PropertyDetailDto>($"api/properties/{id}", ct);

    public static Task<ContractDetailDto> GetContractAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<ContractDetailDto>($"api/contracts/{id}", ct);

    public static Task<InvoiceDetailDto> GetInvoiceAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<InvoiceDetailDto>($"api/invoices/{id}", ct);

    public static Task<IReadOnlyList<PaymentCandidateDto>> GetInvoicePaymentCandidatesAsync(
        this FinanzAppApi api, int id, CancellationToken ct = default)
        => api.GetAsync<IReadOnlyList<PaymentCandidateDto>>($"api/invoices/{id}/payment-candidates", ct);

    public static Task<InvoiceDetailDto> PayInvoiceAsync(
        this FinanzAppApi api, int id, int? transactionId, CancellationToken ct = default)
        => api.SendAsync<PayInvoiceRequest, InvoiceDetailDto>(
            HttpMethod.Post,
            $"api/invoices/{id}/pay",
            new PayInvoiceRequest { TransactionId = transactionId },
            ct);

    // ── Liquidität ─────────────────────────────────────────────────────────────────────────

    public static Task<LiquidityDto> GetLiquidityAsync(this FinanzAppApi api, CancellationToken ct = default)
        => api.GetAsync<LiquidityDto>("api/liquidity", ct);

    public static Task<CashFlowDto> GetCashFlowAsync(
        this FinanzAppApi api, int months, CancellationToken ct = default)
        => api.GetAsync<CashFlowDto>($"api/liquidity/cashflow?months={months}", ct);

    public static Task<SavingsPotentialDto> GetSavingsPotentialAsync(
        this FinanzAppApi api, CancellationToken ct = default)
        => api.GetAsync<SavingsPotentialDto>("api/liquidity/savings", ct);
}
