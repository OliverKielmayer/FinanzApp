using FinanzApp.Api.Application;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Endpoints;

/// <summary>
/// Dokumente, Vorgänge, Gesundheit, Versicherungen, Wohnen und Liquidität.
/// </summary>
/// <remarks>
/// Wie die übrigen Endpunkte: keine Fachlogik, nur Parameter entgegennehmen und einen
/// Application-Service rufen. Alles verlangt eine Anmeldung, schreibende Aufrufe zusätzlich
/// <see cref="AuthPolicies.Write"/>. Welchen Haushalt eine Anfrage sieht, entscheidet weiterhin
/// nicht der Aufrufer, sondern der Mandantenfilter.
/// </remarks>
public static class ExtensionEndpoints
{
    public static void MapExtensions(this IEndpointRouteBuilder app)
    {
        MapDocuments(app);
        MapTasks(app);
        MapHealth(app);
        MapPolicies(app);
        MapCreate(app);
        MapVehicles(app);
        MapScanInbox(app);
        MapScan(app);
        MapHousing(app);
        MapLiquidity(app);
        MapWork(app);
    }

    private static void MapDocuments(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/documents").WithTags("Dokumente").RequireAuthorization();

        api.MapGet("/", async (DocumentArea? area, string? search, DocumentService service, CancellationToken ct)
            => Results.Ok(await service.GetPageAsync(area, search, ct)));

        api.MapGet("/types", async (DocumentService service, CancellationToken ct)
            => Results.Ok(await service.GetTypesAsync(ct)));

        api.MapGet("/search", async (string q, DocumentService service, CancellationToken ct)
            => Results.Ok(await service.SearchAsync(q, ct)));

        api.MapGet("/for/{targetType}/{targetId:int}", async (
                LinkTargetType targetType, int targetId, DocumentService service, CancellationToken ct)
            => Results.Ok(await service.GetForTargetAsync(targetType, targetId, ct)));

        api.MapGet("/{id:int}", async (int id, DocumentService service, CancellationToken ct) =>
        {
            var document = await service.GetAsync(id, ct);
            return document is null ? Results.NotFound() : Results.Ok(document);
        });

        // Liefert die Datei aus. Fehlt sie, ist das kein Serverfehler, sondern der gestaltete
        // Zustand „Datei nicht gefunden“ — der Client zeigt ihn im Detail an.
        api.MapGet("/{id:int}/file", async (int id, DocumentService service, CancellationToken ct) =>
        {
            var file = await service.OpenAsync(id, ct);
            return file is null
                ? Results.NotFound()
                : Results.File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
        });

        // Dateiannahme. Der Schutz vor fremden Formularen liegt beim Anmelde-Cookie mit
        // SameSite=Strict — ein anderer Ursprung bekommt es nicht mitgeschickt.
        api.MapPost("/", async (
            IFormFile file,
            DocumentArea area,
            string? title,
            int? documentTypeId,
            DateOnly? documentDate,
            DocumentService service,
            DocumentPathService paths,
            CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.Problem("Die Datei ist leer.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (file.Length > paths.MaxFileSizeBytes)
            {
                return Results.Problem(
                    $"Die Datei ist größer als {paths.MaxFileSizeBytes / 1024 / 1024} MB.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                await using var content = file.OpenReadStream();
                var result = await service.UploadAsync(
                    content, file.FileName, area, title, documentTypeId, documentDate, ct: ct);

                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write).DisableAntiforgery();

        api.MapPut("/{id:int}", async (
            int id, UpdateDocumentRequest request, DocumentService service, CancellationToken ct) =>
        {
            var document = await service.UpdateAsync(id, request, ct);
            return document is null ? Results.NotFound() : Results.Ok(document);
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapPut("/{id:int}/path", async (
            int id, FixDocumentPathRequest request, DocumentService service, CancellationToken ct) =>
        {
            try
            {
                var document = await service.FixPathAsync(id, request.RelativePath, ct);
                return document is null ? Results.NotFound() : Results.Ok(document);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapPost("/{id:int}/links", async (
            int id, CreateDocumentLinkRequest request, DocumentService service, CancellationToken ct) =>
        {
            try
            {
                var link = await service.LinkAsync(id, request.TargetType, request.TargetId, ct);
                return link is null ? Results.NotFound() : Results.Ok(link);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/links/{linkId:int}", async (
                int linkId, DocumentService service, CancellationToken ct)
            => await service.UnlinkAsync(linkId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/{id:int}", async (int id, DocumentService service, CancellationToken ct)
                => await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Write);
    }

    private static void MapTasks(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/tasks").WithTags("Vorgänge").RequireAuthorization();

        api.MapGet("/", async (TaskState? state, TaskService service, CancellationToken ct)
            => Results.Ok(await service.GetListAsync(state, ct)));

        api.MapGet("/summary", async (TaskService service, CancellationToken ct)
            => Results.Ok(await service.GetSummaryAsync(ct)));

        api.MapPost("/", async (CreateTaskRequest request, TaskService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CreateAsync(request, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapPatch("/{id:int}/state", async (
                int id, UpdateTaskStateRequest request, TaskService service, CancellationToken ct)
            => await service.SetStateAsync(id, request.State, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Write);
    }

    private static void MapHealth(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/health").WithTags("Gesundheit").RequireAuthorization();

        api.MapGet("/bills", async (MedicalBillService service, CancellationToken ct)
            => Results.Ok(await service.GetListAsync(ct)));

        api.MapGet("/bills/{id:int}", async (int id, MedicalBillService service, CancellationToken ct) =>
        {
            var bill = await service.GetAsync(id, ct);
            return bill is null ? Results.NotFound() : Results.Ok(bill);
        });

        api.MapPost("/bills", async (
            CreateMedicalBillRequest request, MedicalBillService service, CancellationToken ct) =>
        {
            try
            {
                var bill = await service.CreateAsync(request, ct);
                return Results.Created($"/api/health/bills/{bill.Id}", bill);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapPatch("/bills/{id:int}/status", async (
            int id, AdvanceMedicalBillRequest request, MedicalBillService service, CancellationToken ct) =>
        {
            var bill = await service.AdvanceAsync(id, request.Status, ct);
            return bill is null ? Results.NotFound() : Results.Ok(bill);
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapGet("/bills/{id:int}/payment-candidates", async (
                int id, MedicalBillService service, CancellationToken ct)
            => Results.Ok(await service.GetPaymentCandidatesAsync(id, ct)));

        api.MapPost("/bills/{id:int}/payment", async (
            int id, LinkPaymentRequest request, MedicalBillService service, CancellationToken ct) =>
        {
            try
            {
                var bill = await service.LinkPaymentAsync(id, request, ct);
                return bill is null ? Results.NotFound() : Results.Ok(bill);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        // Belegerkennung. Ohne angebundene Texterkennung antwortet sie leer — die Maske ist
        // dieselbe, nur unausgefüllt.
        api.MapPost("/extract", async (IFormFile file, IBillTextExtractor extractor, CancellationToken ct) =>
        {
            await using var content = file.OpenReadStream();
            return Results.Ok(await extractor.ExtractAsync(content, file.FileName, ct));
        }).RequireAuthorization(AuthPolicies.Write).DisableAntiforgery();
    }

    /// <summary>Fahrzeuge — dieselbe Form wie Immobilien, weil es dieselbe Art Objekt ist.</summary>
    /// <summary>
    /// Arbeit &amp; Beruf. Die Zuordnung schlägt vor; bestätigt wird sie von Hand, und sie ist
    /// wieder lösbar — dieselbe Mechanik wie bei PKV-Vorgängen und Rechnungen.
    /// </summary>
    private static void MapWork(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/work").WithTags("Arbeit").RequireAuthorization();

        api.MapGet("/", async (EmploymentService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)));

        api.MapPost("/payslips", async (
                CreatePayslipRequest request, EmploymentService service, CancellationToken ct)
            => await Guarded(() => service.CreatePayslipAsync(request, ct)))
            .RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/payslips/{id:int}", async (int id, EmploymentService service, CancellationToken ct)
            => await service.DeletePayslipAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Write);

        api.MapGet("/payslips/{id:int}/payment-candidates", async (
                int id, EmploymentService service, CancellationToken ct)
            => Results.Ok(await service.GetPaymentCandidatesAsync(id, ct)));

        api.MapPost("/payslips/{id:int}/payment", async (
                int id, LinkPayslipPaymentRequest request, EmploymentService service, CancellationToken ct)
            => await Guarded(() => service.LinkPaymentAsync(id, request.TransactionId, ct)))
            .RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/payslips/{id:int}/payment", async (
                int id, EmploymentService service, CancellationToken ct)
            => await Guarded(() => service.DetachPaymentAsync(id, ct)))
            .RequireAuthorization(AuthPolicies.Write);
    }

    /// <summary>Fachliche Verstöße kommen als lesbare 400er zurück, nicht als Absturz.</summary>
    private static async Task<IResult> Guarded<T>(Func<Task<T>> work)
    {
        try
        {
            return Results.Ok(await work());
        }
        catch (RuleViolationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static void MapVehicles(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/vehicles").WithTags("Fahrzeuge").RequireAuthorization();

        api.MapGet("/", async (VehicleService service, CancellationToken ct)
            => Results.Ok(await service.GetListAsync(ct)));

        api.MapGet("/{id:int}", async (int id, VehicleService service, CancellationToken ct) =>
        {
            var vehicle = await service.GetAsync(id, ct);
            return vehicle is null ? Results.NotFound() : Results.Ok(vehicle);
        });
    }

    /// <summary>Der Scaneingang. Wegräumen geht erst, wenn Typ und Objekt stehen.</summary>
    private static void MapScanInbox(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/scan-inbox").WithTags("Scaneingang").RequireAuthorization();

        api.MapGet("/", async (ScanInboxService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)));

        api.MapPost("/{id:int}/file", async (int id, ScanInboxService service, CancellationToken ct)
            => await service.FileAsync(id, ct)
                ? Results.NoContent()
                : Results.Problem(
                    "Der Beleg bleibt im Eingang, bis Typ und Objekt bestimmt sind.",
                    statusCode: StatusCodes.Status409Conflict))
            .RequireAuthorization(AuthPolicies.Write);
    }

    /// <summary>
    /// Belege einlesen — Abschnitt 14 des v5-Handoffs.
    /// </summary>
    /// <remarks>
    /// Zwei Endpunkte für alle Dokumenttypen: analysieren und übernehmen. Welche Felder ein Typ
    /// hat, wohin er gehört und was die Übernahme bewirkt, steht im Typ-Datensatz — nicht in der
    /// Route und nicht in einem Bildschirm.
    /// </remarks>
    private static void MapScan(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/scan").WithTags("Belege").RequireAuthorization();

        api.MapPost("/analyse", async (
            IFormFile file, DocumentScanService service, CancellationToken ct) =>
        {
            await using var content = file.OpenReadStream();

            try
            {
                return Results.Ok(await service.AnalyseAsync(content, file.FileName, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write).DisableAntiforgery();

        // Erst hier verändert sich eine Vermögenszahl, und nur, weil ein Mensch es gesagt hat.
        api.MapPost("/confirm", async (
            ConfirmScanRequest request, DocumentScanService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.ConfirmAsync(request, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);
    }

    /// <summary>
    /// Die Anlege-Flows. Ein Paar Endpunkte für alle Objekttypen: das Formular beschreiben,
    /// das Formular annehmen. Welche Felder es gibt, sagt der Dienst — nicht die Route.
    /// </summary>
    private static void MapCreate(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/create").WithTags("Anlegen").RequireAuthorization();

        api.MapGet("/{type}", async (
            CreateObjectType type, CreateFormService service, CancellationToken ct) =>
        {
            var form = await service.GetFormAsync(type, null, ct);
            return form is null ? Results.NotFound() : Results.Ok(form);
        });

        // Dasselbe Formular, vorbefüllt - plus der Löschabschnitt.
        api.MapGet("/{type}/{id:int}", async (
            CreateObjectType type, int id, CreateFormService service, CancellationToken ct) =>
        {
            var form = await service.GetFormAsync(type, id, ct);
            return form is null ? Results.NotFound() : Results.Ok(form);
        });

        api.MapPut("/{type}/{id:int}", async (
            CreateObjectType type, int id, CreateRequest request,
            CreateFormService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAsync(type, id, request.Values, ct)))
            .RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/{type}/{id:int}", async (
            CreateObjectType type, int id, CreateFormService service, CancellationToken ct)
            => Results.Ok(await service.DeleteAsync(type, id, ct)))
            .RequireAuthorization(AuthPolicies.Write);

        // Police oder Beleg einlesen. Ohne angebundene Analyse antwortet er leer — die Datei
        // ist trotzdem abgelegt, und die Maske ist dieselbe, nur unausgefüllt.
        api.MapPost("/{type}/analyse", async (
            CreateObjectType type, IFormFile file, CreateFormService service, CancellationToken ct) =>
        {
            await using var content = file.OpenReadStream();
            try
            {
                return Results.Ok(await service.AnalyseAsync(type, content, file.FileName, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write).DisableAntiforgery();

        // Übernahme bestätigen. Vorher verändert kein gelesener Wert irgendetwas.
        api.MapPost("/extractions/{documentId:int}/confirm", async (
                int documentId, CreateFormService service, CancellationToken ct)
            => Results.Ok(await service.ConfirmExtractionsAsync(documentId, ct)))
            .RequireAuthorization(AuthPolicies.Write);

        api.MapPost("/{type}", async (
            CreateObjectType type, CreateRequest request, CreateFormService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(type, request.Values, ct);

            // Auch das Scheitern ist eine gültige Antwort: die Oberfläche braucht das Feld,
            // nicht nur einen Statuscode.
            return Results.Ok(result);
        }).RequireAuthorization(AuthPolicies.Write);
    }

    private static void MapPolicies(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/policies").WithTags("Vorsorge & Absicherung").RequireAuthorization();

        api.MapGet("/vorsorge", async (PolicyService service, CancellationToken ct)
            => Results.Ok(await service.GetOverviewAsync(capitalForming: true, ct)));

        api.MapGet("/absicherung", async (PolicyService service, CancellationToken ct)
            => Results.Ok(await service.GetOverviewAsync(capitalForming: false, ct)));

        api.MapGet("/{id:int}", async (int id, PolicyService service, CancellationToken ct) =>
        {
            var policy = await service.GetAsync(id, ct);
            return policy is null ? Results.NotFound() : Results.Ok(policy);
        });
    }

    private static void MapHousing(IEndpointRouteBuilder app)
    {
        var properties = app.MapGroup("/api/properties").WithTags("Wohnen").RequireAuthorization();

        properties.MapGet("/", async (PropertyService service, CancellationToken ct)
            => Results.Ok(await service.GetListAsync(ct)));

        properties.MapGet("/{id:int}", async (int id, PropertyService service, CancellationToken ct) =>
        {
            var property = await service.GetAsync(id, ct);
            return property is null ? Results.NotFound() : Results.Ok(property);
        });

        var contracts = app.MapGroup("/api/contracts").WithTags("Wohnen").RequireAuthorization();

        contracts.MapGet("/{id:int}", async (int id, PropertyService service, CancellationToken ct) =>
        {
            var contract = await service.GetContractAsync(id, ct);
            return contract is null ? Results.NotFound() : Results.Ok(contract);
        });

        var invoices = app.MapGroup("/api/invoices").WithTags("Wohnen").RequireAuthorization();

        invoices.MapGet("/{id:int}", async (int id, PropertyService service, CancellationToken ct) =>
        {
            var invoice = await service.GetInvoiceAsync(id, ct);
            return invoice is null ? Results.NotFound() : Results.Ok(invoice);
        });

        invoices.MapGet("/{id:int}/payment-candidates", async (
                int id, PropertyService service, CancellationToken ct)
            => Results.Ok(await service.GetPaymentCandidatesAsync(id, ct)));

        invoices.MapPost("/{id:int}/pay", async (
            int id, PayInvoiceRequest request, PropertyService service, CancellationToken ct) =>
        {
            try
            {
                var invoice = await service.PayAsync(id, request, ct);
                return invoice is null ? Results.NotFound() : Results.Ok(invoice);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);
    }

    private static void MapLiquidity(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/liquidity").WithTags("Liquidität").RequireAuthorization();

        api.MapGet("/", async (LiquidityService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)));

        api.MapGet("/cashflow", async (int? months, LiquidityService service, CancellationToken ct)
            => Results.Ok(await service.GetCashFlowAsync(months ?? LiquidityService.DefaultMonths, ct)));

        api.MapGet("/savings", async (LiquidityService service, CancellationToken ct)
            => Results.Ok(await service.GetSavingsPotentialAsync(ct)));
    }
}
