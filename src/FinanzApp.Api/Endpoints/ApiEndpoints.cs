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
            catch (ArgumentException ex)
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
            catch (ArgumentException ex)
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
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapGet("/categories", async (
                CategoryDirection? direction, CatalogService service, CancellationToken ct)
            => Results.Ok(await service.GetCategoriesAsync(direction, ct)));

        api.MapGet("/rules", async (CatalogService service, CancellationToken ct)
            => Results.Ok(await service.GetRulesAsync(ct)));

        api.MapGet("/budgets", async (BudgetPeriod? period, BudgetService service, CancellationToken ct)
            => Results.Ok(await service.GetOverviewAsync(period ?? BudgetPeriod.Month, ct)));

        api.MapGet("/portfolio", async (PortfolioService service, CancellationToken ct) =>
        {
            var portfolio = await service.GetAsync(ct);
            return portfolio is null ? Results.NotFound() : Results.Ok(portfolio);
        });

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

        api.MapPost("/import/{id:guid}/commit", async (Guid id, ImportService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CommitAsync(id, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization(AuthPolicies.Write);

        api.MapGet("/overview/more", async (OverviewService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)));
    }
}
