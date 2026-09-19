using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory.Obligations;
using Tawaka.Infrastructure.Administration;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Every material payroll action leaves a record naming who did it and when.
/// <para>
/// Individual pieces of this are tested elsewhere. What this adds is coverage: running the things
/// a payroll office actually does and then asking the audit trail whether it saw each of them.
/// </para>
/// </summary>
public class QaAuditCoverageTests : PayrollFixtureBase
{
    [Fact]
    public async Task Everything_material_leaves_an_audited_record()
    {
        using var fixture = await SetUpAsync();
        var services = PayrollServices.For(fixture.Db);

        // A user, so user administration is on the record too.
        var administration = new Application.Security.UserAdministrationService(
            fixture.Db.Context, fixture.Db.User, fixture.Db.Hasher,
            new Application.Security.RoleService(fixture.Db.Context, fixture.Db.User),
            fixture.Db.Clock);

        var role = await fixture.Db.Context.Roles.AsNoTracking()
            .SingleAsync(r => r.Name == RoleNames.PayrollOfficer);

        Assert.True((await administration.CreateAsync(new Application.Security.CreateUserCommand
        {
            Username = "audited.officer",
            FullName = "Audited Officer",
            RoleIds = new[] { role.Id }
        })).Succeeded);

        // A contract change, so contract history is on the record.
        fixture.Db.Context.ChangeTracker.Clear();
        var contracts = new Application.Employees.EmployeeContractService(
            fixture.Db.Context, fixture.Db.User);
        Assert.True((await contracts.SupersedeAsync(
            fixture.Employee.Id,
            NewContract(fixture.CompanyId, fixture.Employee.Id, fixture.EmploymentTypeId, 950m),
            new DateOnly(2026, 10, 1),
            "Annual review.")).Succeeded);

        // The payroll itself, through to locked.
        fixture.Db.Context.ChangeTracker.Clear();
        var runId = await RunThroughFinaliseAsync(fixture);

        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == runId);

        // A payslip, then a re-issue.
        fixture.Db.Context.ChangeTracker.Clear();
        var issued = await services.Payslips.GenerateAsync(runEmployee.Id);
        Assert.True(issued.Succeeded, issued.Validation.ToString());

        fixture.Db.Context.ChangeTracker.Clear();
        var reissued = await services.Payslips.GenerateAsync(
            runEmployee.Id, "Bank details were wrong on the first issue.");
        Assert.True(reissued.Succeeded, reissued.Validation.ToString());

        // A statutory payment.
        fixture.Db.Context.ChangeTracker.Clear();
        var obligation = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .FirstAsync(o => o.PayrollRunId == runId &&
                             o.ObligationType == StatutoryObligationType.Paye);

        Assert.True((await services.Obligations.ApproveAsync(obligation.Id)).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        Assert.True((await services.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = obligation.Id,
            Amount = obligation.CalculatedAmount,
            CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8),
            PaymentReference = "RTGS-AUDIT-1"
        })).Succeeded);

        // Net wages paid, then locked.
        fixture.Db.Context.ChangeTracker.Clear();
        Assert.True((await services.Runs.MarkNetWagesPaidAsync(runId)).IsValid);
        fixture.Db.Context.ChangeTracker.Clear();
        Assert.True((await services.Runs.LockAsync(runId)).IsValid);

        // A backup, and a restore of it.
        var backups = new BackupService(fixture.Db.Context, fixture.Db.User, fixture.Db.Clock);
        var root = Path.Combine(Path.GetTempPath(), $"tawaka-audit-{Guid.NewGuid():N}");

        try
        {
            fixture.Db.Context.ChangeTracker.Clear();
            var backup = await backups.BackupAsync(root, "Month end");
            Assert.True(backup.Succeeded, backup.Validation.ToString());

            // Taking a backup is recorded — and recorded *after* the copy, so the entry is in the
            // live database and not inside the backup it describes. Asserted here, because the
            // restore below puts the database back to the moment before it was written.
            fixture.Db.Context.ChangeTracker.Clear();
            Assert.Contains(
                await fixture.Db.Context.AuditLogs.AsNoTracking().ToListAsync(),
                e => e.EntityName == "Backup" &&
                     e.Action == AuditAction.Export &&
                     e.Reason == "Month end");

            fixture.Db.Context.ChangeTracker.Clear();
            Assert.True((await backups.RestoreAsync(
                backup.Value!.BackupPath, confirmed: true)).Succeeded);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // ---- What the audit trail saw -----------------------------------------------------------
        fixture.Db.Context.ChangeTracker.Clear();
        var audit = await fixture.Db.Context.AuditLogs.AsNoTracking().ToListAsync();

        var recorded = audit.Select(e => e.EntityName).Distinct().OrderBy(n => n).ToList();

        foreach (var expected in new[]
                 {
                     nameof(Employee), nameof(EmployeeContract), nameof(PayrollRun),
                     nameof(Payslip), nameof(StatutoryObligation), nameof(StatutoryPayment),
                     nameof(User), "Restore"
                 })
        {
            Assert.True(
                recorded.Contains(expected),
                $"Nothing in the audit trail records a change to {expected}. " +
                $"It holds: {string.Join(", ", recorded)}");
        }

        // Each entry says who, when and where — an audit trail that forgets the actor is not one.
        Assert.All(audit, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.UserId));
            Assert.False(string.IsNullOrWhiteSpace(entry.UserName));
            Assert.NotEqual(default, entry.OccurredAt);
            Assert.False(string.IsNullOrWhiteSpace(entry.EntityId));
        });

        // The run's lock is a recorded change of status, with the old value kept.
        var lockEntry = Assert.Single(audit, e =>
            e.EntityName == nameof(PayrollRun) &&
            e.FieldName == nameof(PayrollRun.Status) &&
            e.NewValue == nameof(PayrollRunStatus.Locked));
        Assert.Equal(nameof(PayrollRunStatus.Paid), lockEntry.OldValue);

        // Re-issuing a payslip supersedes rather than replaces, and says why.
        var payslips = await fixture.Db.Context.Payslips.AsNoTracking()
            .Where(p => p.PayrollRunEmployeeId == runEmployee.Id)
            .OrderBy(p => p.Revision)
            .ToListAsync();

        Assert.Equal(2, payslips.Count);
        Assert.Contains(audit, e => e.EntityName == nameof(Payslip) && e.Action == AuditAction.Create);

        // The restore entry is in the restored database, because it was written after the swap —
        // it is the first thing in the recovered history that says the history was recovered.
        var restore = Assert.Single(audit, e => e.EntityName == "Restore");
        Assert.Contains("restored from a backup", restore.NewValue!);
        Assert.Equal(fixture.Db.User.UserId, restore.UserId);
    }

    /// <summary>
    /// The audit trail is append-only, and nothing in the application removes from it or changes
    /// it. This is checked against the source, because it is an absence and an absence cannot be
    /// demonstrated by calling something.
    /// </summary>
    [Fact]
    public void Nothing_in_the_application_edits_or_deletes_the_audit_trail()
    {
        var source = new DirectoryInfo(AppContext.BaseDirectory);

        while (source is not null && !Directory.Exists(Path.Combine(source.FullName, "src")))
        {
            source = source.Parent;
        }

        Assert.NotNull(source);

        var offending = Directory
            .EnumerateFiles(Path.Combine(source!.FullName, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor")) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => new { File = f, Text = File.ReadAllText(f) })
            .Where(f =>
                f.Text.Contains("AuditLogs.Remove", StringComparison.Ordinal) ||
                f.Text.Contains("AuditLogs.RemoveRange", StringComparison.Ordinal) ||
                f.Text.Contains("AuditLogs.Update", StringComparison.Ordinal) ||
                f.Text.Contains("AuditLogs.ExecuteDelete", StringComparison.Ordinal) ||
                f.Text.Contains("AuditLogs.ExecuteUpdate", StringComparison.Ordinal))
            .Select(f => f.File)
            .ToList();

        Assert.Empty(offending);
    }
}
