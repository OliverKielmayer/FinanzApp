using FinanzApp.Api.Application;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Endpoints;

/// <summary>
/// Die HTTP-Oberfläche der Anwendung. Die Endpunkte enthalten keine Fachlogik — sie nehmen
/// Parameter entgegen, rufen einen Application-Service und geben dessen Ergebnis zurück.
/// </summary>
/// <remarks>
/// Die ganze Gruppe verlangt eine Anmeldung; welchen Haushalt eine Anfrage sieht, entscheidet
/// nicht der Aufrufer, sondern der Mandantenfilter im <c>DbContext</c>. Schreibende Endpunkte
/// verlangen zusätzlich <see cref="AuthPolicies.Write"/>.
/// </remarks>
public static class ApiEndpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("FinanzApp").RequireAuthorization();

        api.MapGet("/dashboard", async (DashboardService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)));

        api.MapGet("/accounts", async (AccountService service, CancellationToken ct)
            => Results.Ok(await service.GetAccountsAsync(ct)));

        // Die Gemeinschaftskonten mit Soll und Eingang. Fertig gerechnet: der Schirm stellt
        // gegenüber, er rechnet nicht.
        api.MapGet("/accounts/gemeinschaft", async (
                ParticipationService service, IClock clock, CancellationToken ct)
            => Results.Ok(await service.JointAccountsAsync(clock.Today, ct)));

        api.MapGet("/transactions", async (
                string? search, int? account, int? category, TransactionKind? kind, bool? offen,
                int? skip, int? take, TransactionService service, CancellationToken ct)
            => Results.Ok(await service.GetPageAsync(
                search, account, category, kind, offen ?? false,
                skip ?? 0, Math.Clamp(take ?? 100, 1, 500), ct)));

        api.MapPost("/transactions", async (
            CreateTransactionRequest request, TransactionService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/transactions/{created.Id}", created);
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/transactions/{id:int}", async (
                int id, TransactionService service, CancellationToken ct)
            => await service.DeleteAsync([id], ct) > 0 ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Write);

        api.MapPost("/transactions/batch-delete", async (
                BatchAssignRequest request, TransactionService service, CancellationToken ct)
            => Results.Ok(await service.DeleteAsync(request.TransactionIds, ct)))
            .RequireAuthorization(AuthPolicies.Write);

        api.MapPost("/transactions/batch-category", async (
            BatchAssignRequest request, TransactionService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.AssignCategoryBatchAsync(request, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapPatch("/transactions/{id:int}/category", async (
            int id, AssignCategoryRequest request, TransactionService service, CancellationToken ct) =>
        {
            try
            {
                var updated = await service.AssignCategoryAsync(id, request, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapGet("/categories", async (
                CategoryDirection? direction, CatalogService service, CancellationToken ct)
            => Results.Ok(await service.GetCategoriesAsync(direction, ct)));

        // Kategorien sind Daten, keine Konstante im Code: sie speisen die Chips bei Erfassung,
        // Kategorie-Sheet, Import und Budgetanlage.
        // Die Freigabe eines Kontos aendern. Wer nicht Eigentuemer ist, wird abgewiesen -
        // der Filter im DbContext schuetzt das Lesen, diese Pruefung das Schreiben.
        api.MapPut("/accounts/{id:int}/sharing", async (
            int id, AccountSharingRequest request, AccountService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.SetSharingAsync(id, request.Sharing, request.UserIds, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        // Administration: der Pflegescreen gehört hinter dieselbe Schranke wie die
        // Benutzerverwaltung. Lesen darf jeder — die Typen stehen ohnehin an jedem Dokument.
        api.MapGet("/document-types", async (DocumentTypeService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)));

        api.MapPost("/document-types", async (
            DocumentTypeNameRequest request, DocumentTypeService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CreateAsync(request, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.ManageUsers);

        api.MapPatch("/document-types/{id:int}", async (
            int id, DocumentTypeNameRequest request, DocumentTypeService service,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.RenameAsync(id, request.Name, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.ManageUsers);

        api.MapDelete("/document-types/{id:int}", async (
            int id, DocumentTypeService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.DeleteAsync(id, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        }).RequireAuthorization(AuthPolicies.ManageUsers);

        api.MapGet("/categories/usage", async (
                CategoryDirection direction, CatalogService service, CancellationToken ct)
            => Results.Ok(await service.GetUsageAsync(direction, ct)));

        api.MapPost("/categories", async (
            CategoryNameRequest request, CatalogService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CreateAsync(request.Name, request.Direction, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        // Anlegen oder finden — der Import braucht beides in einem Schritt.
        api.MapPost("/categories/ensure", async (
            CategoryNameRequest request, CatalogService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.EnsureAsync(request.Name, request.Direction, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapPatch("/categories/{id:int}", async (
            int id, CategoryNameRequest request, CatalogService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.RenameAsync(id, request.Name, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        // Die steuerliche Einordnung. Ohne sie bleiben Handwerkerleistungen und
        // Werbungskosten im Steuerjahr leer, und niemand kann sagen warum.
        api.MapPatch("/categories/{id:int}/tax", async (
            int id, CategoryTaxRequest request, CatalogService service, CancellationToken ct) =>
        {
            try
            {
                return await service.SetTaxCategoryAsync(id, request.TaxCategory, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        // Objektbezogen oder Lebenshaltung. Ohne die Trennung wäre jede €/m²-Zahl falsch,
        // weil Lebensmittel vom selben Konto abgehen wie der Strom für das Haus.
        api.MapPatch("/categories/{id:int}/objekt", async (
            int id, CategoryPropertyRequest request, CatalogService service, CancellationToken ct) =>
        {
            try
            {
                return await service.SetPropertyRelatedAsync(id, request.PropertyRelated, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/categories/{id:int}", async (
            int id, CatalogService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.DeleteAsync(id, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/rules/{id:int}", async (int id, CatalogService service, CancellationToken ct)
                => await service.DeleteRuleAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Write);

        api.MapGet("/rules", async (CatalogService service, CancellationToken ct)
            => Results.Ok(await service.GetRulesAsync(ct)));

        api.MapGet("/budgets", async (PeriodScope? period, BudgetService service, CancellationToken ct)
            => Results.Ok(await service.GetOverviewAsync(period ?? PeriodScope.Month, ct)));

        // Als POST, weil die Ausschlussliste beliebig lang wird — in einer Abfragezeichenkette
        // waere sie irgendwann abgeschnitten, und ein Bericht rechnete stillschweigend anders,
        // als die Oberflaeche zeigt.
        api.MapPost("/reports/cost-trend", async (
                CostTrendRequest request, ReportService service, CancellationToken ct)
            => Results.Ok(await service.GetCostTrendAsync(request, ct)));

        api.MapPost("/reports/fixed-costs", async (
                FixedCostsRequest request, ReportService service, CancellationToken ct)
            => Results.Ok(await service.GetFixedCostsAsync(request, ct)));

        // Ohne Depot im Bestand gibt es nichts zu berichten — 404 statt einer Hülle aus Nullen.
        api.MapGet("/reports/portfolio-gain", async (
            int? depot, ReportService service, CancellationToken ct) =>
        {
            var gewinn = await service.GetPortfolioGainAsync(depot, ct);
            return gewinn is null ? Results.NotFound() : Results.Ok(gewinn);
        });

        // Kalenderjahr statt Berichtsrahmen: Eigenanteile und Beiträge zählen jahresweise.
        api.MapGet("/reports/health-balance", async (
                int? jahr, bool? alle, HealthBalanceService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(jahr, alle == true, ct)));

        // ── Kurse ─────────────────────────────────────────────────────────────────────
        //
        // Abgerufen wird nur hier und nach Zeitplan, nie beim Seitenaufruf: die Quelle ist
        // inoffiziell, und ein Abruf je Betrachter wäre der schnellste Weg zur Sperre.
        api.MapGet("/quotes/band", async (QuoteService service, CancellationToken ct)
            => Results.Ok(await service.GetBandAsync(ct)));

        api.MapGet("/quotes/{isin}", async (
                string isin, QuoteRange? zeitraum, decimal? einstand,
                QuoteService service, CancellationToken ct)
            => Results.Ok(await service.GetSeriesAsync(
                isin, zeitraum ?? QuoteRange.Year, einstand, ct)));

        api.MapPost("/quotes/refresh", async (QuoteService service, CancellationToken ct)
                => Results.Ok(await service.RefreshAsync(manual: true, ct)))
            .RequireAuthorization(AuthPolicies.Write);

        // Kandidaten mit Belegbezug, keine Steuerberechnung — der Bericht sagt das auch selbst.
        api.MapGet("/reports/tax-year", async (
                int? jahr, TaxYearService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(jahr, ct)));

        api.MapGet("/reports/data-quality", async (ReportService service, CancellationToken ct)
            => Results.Ok(await service.GetDataQualityAsync(ct)));

        api.MapGet("/reports/views", async (ReportService service, CancellationToken ct)
            => Results.Ok(await service.GetViewsAsync(ct)));

        api.MapPost("/reports/views", async (
            SaveReportViewRequest request, ReportService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.SaveViewAsync(request, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        api.MapDelete("/reports/views/{id:int}", async (
                int id, ReportService service, CancellationToken ct)
            => await service.DeleteViewAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // Ein Aggregat statt sieben Abrufe, die der Client zusammenlegt.
        api.MapGet("/holdings", async (
                HoldingClass? klasse, HoldingsService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(klasse, ct)));

        api.MapGet("/portfolio", async (PortfolioService service, CancellationToken ct) =>
        {
            var portfolio = await service.GetAsync(ct);
            return portfolio is null ? Results.NotFound() : Results.Ok(portfolio);
        });

        api.MapGet("/portfolio/{depotId:int}/trades", async (
                int depotId, int? jahr, DepotTradeService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(depotId, jahr, ct)));

        // Der Import liegt bewusst nicht am Ende der Liste, sondern im Kopf des Reiters: bei
        // 26 Ausführungen wäre er noch zu finden, bei 800 nicht mehr.
        api.MapPost("/portfolio/{depotId:int}/trades", async (
            int depotId, IFormFile file, DepotTradeService service, CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.Problem(
                    "Die Datei ist leer.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (file.Length > OrderCsvParser.MaxBytes)
            {
                return Results.Problem(
                    "Die Datei ist groesser als 4 MB.", statusCode: StatusCodes.Status400BadRequest);
            }

            await using var content = file.OpenReadStream();

            try
            {
                return Results.Ok(await service.ImportAsync(depotId, content, file.FileName, ct));
            }
            catch (Exception ex) when (ex is StatementFormatException or RuleViolationException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write).DisableAntiforgery();

        api.MapGet("/portfolio/{depotId:int}/statements", async (
                int depotId, DepotStatementService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(depotId, ct)));

        api.MapPost("/portfolio/{depotId:int}/statements", async (
            int depotId, CreateDepotStatementRequest request, DepotStatementService service,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CreateAsync(depotId, request, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapDelete("/portfolio/statements/{id:int}", async (
                int id, DepotStatementService service, CancellationToken ct)
            => await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Write);

        // Muss vor der Id-Route stehen, damit „primary“ nicht als Id gelesen wird.
        api.MapGet("/loans/primary", async (int? months, LoanService service, CancellationToken ct) =>
        {
            var id = await service.GetPrimaryLoanIdAsync(ct);
            if (id is null)
            {
                return Results.NotFound();
            }

            var loan = await service.GetAsync(
                id.Value, Math.Clamp(months ?? LoanService.DefaultScheduleMonths, 1, 480), ct);
            return loan is null ? Results.NotFound() : Results.Ok(loan);
        });

        api.MapGet("/loans/{id:int}", async (int id, int? months, LoanService service, CancellationToken ct) =>
        {
            var loan = await service.GetAsync(id, Math.Clamp(months ?? LoanService.DefaultScheduleMonths, 1, 480), ct);
            return loan is null ? Results.NotFound() : Results.Ok(loan);
        });

        api.MapGet("/import/preview", async (ImportService service, CancellationToken ct)
            => Results.Ok(await service.GetPreviewAsync(ct)));

        // Eine hochgeladene Auszugsdatei. DisableAntiforgery wie bei den uebrigen Uploads:
        // der Schutz gegen fremde Formulare liegt am Anmelde-Cookie mit SameSite=Strict.
        api.MapPost("/import/read", async (
            IFormFile file, ImportService service, CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.Problem(
                    "Die Datei ist leer.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (file.Length > CamtStatementParser.MaxBytes)
            {
                return Results.Problem(
                    "Die Datei ist groesser als 20 MB.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await using var content = file.OpenReadStream();
            try
            {
                return Results.Ok(await service.ReadAsync(content, file.FileName, ct));
            }
            catch (StatementFormatException ex)
            {
                // Der Grund steht in der Meldung und gehoert dem Benutzer — er hat die
                // Datei ausgesucht und kann eine andere nehmen.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write).DisableAntiforgery();

        api.MapPost("/import/commit", async (
            ImportCommitRequest request, ImportService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CommitAsync(request, ct));
            }
            catch (RuleViolationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapGet("/overview/more", async (OverviewService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)));
    }
}
