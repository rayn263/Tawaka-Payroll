using Bunit;
using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;
using Tawaka.Ui.Shared.Components.Pages;

namespace Tawaka.Ui.Tests;

/// <summary>
/// Verifying a statutory rule is the only way an installation ever leaves
/// COMPLIANCE-UNVERIFIED, so it has to be possible — and it has to be deliberate.
/// </summary>
public sealed class StatutoryVerificationUiTests : UiTestHost
{
    private IRenderedComponent<StatutoryRules> Rules()
    {
        SignInWithEverything();
        return RenderComponent<StatutoryRules>();
    }

    [Fact]
    public void A_rule_can_be_verified_against_a_named_document()
    {
        var page = Rules();
        var before = Db.StatutoryRules.AsNoTracking()
            .Count(r => r.VerificationStatus == VerificationStatus.Verified);
        Assert.Equal(0, before);

        page.FindAll("button").First(b => b.TextContent.Trim() == "Verify").Click();

        Field(page, "Document").Change("ZIMRA PAYE tables 2026");
        Field(page, "Where in it").Change("Monthly USD table, page 2");
        Field(page, "Date of the document").Change("2026-01-01");

        Button(page, "Mark verified").Click();

        Db.ChangeTracker.Clear();
        var verified = Db.StatutoryRules.AsNoTracking()
            .Include(r => r.Source)
            .Where(r => r.VerificationStatus == VerificationStatus.Verified)
            .ToList();

        var rule = Assert.Single(verified);
        Assert.Equal("ZIMRA PAYE tables 2026", rule.Source!.Source);
        Assert.Equal("Monthly USD table, page 2", rule.Source.SourceReference);
        Assert.Equal(new DateOnly(2026, 1, 1), rule.Source.SourceDate);
        Assert.Equal("QA Administrator", rule.Source.VerifiedBy);
        Assert.NotNull(rule.Source.VerifiedAt);
    }

    [Fact]
    public void A_rule_cannot_be_verified_against_nothing()
    {
        var page = Rules();

        page.FindAll("button").First(b => b.TextContent.Trim() == "Verify").Click();
        Field(page, "Document").Change(string.Empty);
        Button(page, "Mark verified").Click();

        Assert.Contains("cannot be verified against", page.Markup);
        Assert.Equal(
            0,
            Db.StatutoryRules.AsNoTracking()
                .Count(r => r.VerificationStatus == VerificationStatus.Verified));
    }

    [Fact]
    public void A_verification_can_be_withdrawn_but_only_with_a_reason()
    {
        var page = Rules();

        page.FindAll("button").First(b => b.TextContent.Trim() == "Verify").Click();
        Field(page, "Document").Change("ZIMRA PAYE tables 2026");
        Button(page, "Mark verified").Click();

        page.FindAll("button").First(b => b.TextContent.Trim() == "Withdraw").Click();
        Button(page, "Withdraw verification").Click();
        Assert.Contains("Say why", page.Markup);

        Db.ChangeTracker.Clear();
        Assert.Equal(
            1,
            Db.StatutoryRules.AsNoTracking()
                .Count(r => r.VerificationStatus == VerificationStatus.Verified));

        Field(page, "Reason").Change("The 2026 table was superseded in March.");
        Button(page, "Withdraw verification").Click();

        Db.ChangeTracker.Clear();
        Assert.Equal(
            0,
            Db.StatutoryRules.AsNoTracking()
                .Count(r => r.VerificationStatus == VerificationStatus.Verified));
    }

    [Fact]
    public void A_user_without_the_permission_is_offered_no_way_to_verify_anything()
    {
        SignIn("Officer", RoleNames.PayrollOfficer, Permissions.StatutoryView);

        var page = RenderComponent<StatutoryRules>();

        Assert.DoesNotContain(">Verify<", page.Markup);
        Assert.DoesNotContain("Mark verified", page.Markup);
    }
}
