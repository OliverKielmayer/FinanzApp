using System.Security.Claims;
using FinanzApp.Client.Services;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace FinanzApp.Tests;

/// <summary>
/// Der begonnene Import überdauert den Bereichswechsel.
/// </summary>
/// <remarks>
/// Anlass ist der Satz aus §8d, der über das Kategorieanlegen hinausreicht: ein Verlassen des
/// Flusses darf nie eingegebene Arbeit verwerfen. Bei dreihundert Sätzen sind das Dutzende
/// Zuordnungen — lägen sie in der Seite, wäre ein Blick in die Kategorienliste ein Neuanfang.
/// Diese Tests halten fest, was bleibt und was ausdrücklich nicht bleibt.
/// </remarks>
public sealed class ImportDraftTests
{
    /// <summary>Meldet einen Benutzerwechsel — mehr braucht der Entwurf davon nicht.</summary>
    private sealed class StubAuth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        public void Change() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>Eine Uhr, die stillsteht, bis der Test sie weiterstellt.</summary>
    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private readonly StubAuth auth = new();
    private readonly StoppedClock clock = new();

    private ImportDraft Draft() => new(auth, clock);

    private static ImportPreviewDto Preview(params bool[] preSelected) => new()
    {
        Id = Guid.NewGuid(),
        FileName = "camt052.xml",
        BankName = "Sparkasse",
        Format = "CAMT.052",
        ProfileName = "Sparkasse camt.052",
        DuplicateCriterion = "egal",
        RecordCount = preSelected.Length,
        NewCount = preSelected.Count(x => x),
        DuplicateCount = 0,
        ExistingCount = 0,
        ErrorCount = 0,
        SuggestedAccountId = 7,
        Accounts = [new ImportAccountDto { Id = 7, Name = "Sparkasse Giro" }],
        Rows =
        [
            .. preSelected.Select((neu, i) => new ImportRowDto
            {
                Index = i,
                Payee = $"Empfänger {i}",
                State = neu ? ImportRowState.New : ImportRowState.Existing,
                PreSelected = neu,
            }),
        ],
    };

    [Fact]
    public void Ohne_gelesene_Datei_gibt_es_keine_Arbeit()
    {
        var draft = Draft();

        Assert.False(draft.HasWork);
        Assert.False(draft.Expired);
        Assert.Null(draft.Preview);
    }

    [Fact]
    public void Einlesen_uebernimmt_Vorauswahl_und_erkanntes_Konto()
    {
        var draft = Draft();
        draft.Start(Preview(true, false, true));

        Assert.True(draft.HasWork);
        Assert.Equal(7, draft.AccountId);
        Assert.Equal([0, 2], draft.Selected.Order());
    }

    [Fact]
    public void Die_Entscheidungen_bleiben_liegen()
    {
        var draft = Draft();
        draft.Start(Preview(true, true));

        draft.Choices["rewe"] = new ImportCategoryChoice("REWE", 3, RememberRule: true);
        draft.Deferred.Add("dm");
        draft.KeepOverrides[1] = new ImportKeepFields(Purpose: false);
        draft.AccountId = 9;
        draft.OpenGroup = "rewe";
        draft.FreshCategory = "Abo";
        draft.Selected.Remove(1);

        // Der Dienst lebt weiter, wenn die Seite verschwindet — genau das ist der Zweck.
        Assert.Equal(3, draft.Choices["rewe"].CategoryId);
        Assert.Contains("dm", draft.Deferred);
        Assert.False(draft.KeepOverrides[1].Purpose);
        Assert.Equal(9, draft.AccountId);
        Assert.Equal("rewe", draft.OpenGroup);
        Assert.Equal("Abo", draft.FreshCategory);
        Assert.Equal([0], draft.Selected);
    }

    [Fact]
    public void Eine_neue_Datei_faengt_von_vorn_an()
    {
        var draft = Draft();
        draft.Start(Preview(true, true));

        draft.Choices["rewe"] = new ImportCategoryChoice("REWE", 3, RememberRule: true);
        draft.Deferred.Add("dm");
        draft.Grouped = false;

        // Zuordnungen gehoeren zu der Datei, fuer die sie getroffen wurden.
        draft.Start(Preview(true));

        Assert.Empty(draft.Choices);
        Assert.Empty(draft.Deferred);
        Assert.True(draft.Grouped);
        Assert.Equal([0], draft.Selected);
    }

    [Fact]
    public void Verwerfen_laesst_nichts_stehen()
    {
        var draft = Draft();
        draft.Start(Preview(true));
        draft.Choices["rewe"] = new ImportCategoryChoice("REWE", 3, RememberRule: false);

        draft.Clear();

        Assert.False(draft.HasWork);
        Assert.Empty(draft.Selected);
        Assert.Empty(draft.Choices);
        Assert.Equal(0, draft.AccountId);
    }

    [Fact]
    public void Beim_Benutzerwechsel_bleibt_kein_fremder_Auszug_liegen()
    {
        var draft = Draft();
        draft.Start(Preview(true));

        // Ein Auszug nennt Empfaenger, Betraege und IBANs. Der naechste Benutzer am selben
        // Geraet hat damit nichts zu tun.
        auth.Change();

        Assert.False(draft.HasWork);
        Assert.Empty(draft.Selected);
    }

    [Fact]
    public void Nach_der_Vorschaufrist_ist_der_Entwurf_abgelaufen()
    {
        var draft = Draft();
        draft.Start(Preview(true));

        clock.Advance(ImportPreviewCache.Lifetime - TimeSpan.FromMinutes(1));
        Assert.False(draft.Expired);

        // Danach hat der Server die Datei weggeraeumt — der Entwurf zeigt ins Leere.
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(draft.Expired);
    }
}
