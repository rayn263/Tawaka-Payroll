using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Calendars;
using Tawaka.Application.Employees;
using Tawaka.Application.Leave;
using Tawaka.Application.Loans;
using Tawaka.Application.Release;
using Tawaka.Application.Reports;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Application.Time;
using Tawaka.Domain.Calendars;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Loans;
using Tawaka.Domain.Organisation;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Time;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Domain.Statutory;
using Tawaka.Domain.Statutory.Obligations;
using Tawaka.Infrastructure.Interceptors;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The whole journey, in one test, against the real services and the real database.
/// <para>
/// Company setup → employee → contract → earnings → project and site → time → overtime → leave →
/// loan → input approval → period → run → snapshot → calculate → explain → review → approve →
/// finalise → obligations → net wages paid → statutory payment → payslip → reports → lock →
/// attempt modification → refused.
/// </para>
/// <para>
/// Unit tests prove each part behaves. This proves the parts fit together, which is a different
/// claim and the one a business actually cares about.
/// </para>
/// </summary>
public class EndToEndPayrollTests : EmployeeTestBase
{
    private sealed record Actors(
        PayrollServices Officer, PayrollServices Manager, PayrollServices Administrator,
        string OfficerId = "u-officer", string ManagerId = "u-manager");

    private static Actors Build(TestDatabase db) => new(
        PayrollServices.For(db, new TestUser("u-officer", "Tapiwa Ncube")),
        PayrollServices.For(db, new TestUser("u-manager", "Rudo Moyo")),
        PayrollServices.For(db, new TestUser("u-admin", "Anesu Chirwa")));

    /// <summary>
    /// The officer and the manager, created through the application the way a business creates
    /// them, so the journey below is driven by accounts that actually exist rather than by test
    /// identities. Approval turns on who somebody is, so this is not a detail.
    /// </summary>
    private static async Task<Actors> BuildFromCreatedUsersAsync(TestDatabase db)
    {
        var administration = new Tawaka.Application.Security.UserAdministrationService(
            db.Context, db.User, db.Hasher,
            new Tawaka.Application.Security.RoleService(db.Context, db.User), db.Clock);

        async Task<(PayrollServices Services, string UserId)> CreateAsync(
            string username, string fullName, string roleName)
        {
            var role = await db.Context.Roles.AsNoTracking()
                .SingleAsync(r => r.Name == roleName);

            var created = await administration.CreateAsync(
                new Tawaka.Application.Security.CreateUserCommand
                {
                    Username = username,
                    FullName = fullName,
                    RoleIds = new[] { role.Id }
                });

            Assert.True(created.Succeeded, created.Validation.ToString());
            db.Context.ChangeTracker.Clear();

            var userId = created.Value!.UserId.ToString();
            return (PayrollServices.For(db, new TestUser(userId, fullName)), userId);
        }

        var (officer, officerId) = await CreateAsync("t.ncube", "Tapiwa Ncube", RoleNames.PayrollOfficer);
        var (manager, managerId) = await CreateAsync("r.moyo", "Rudo Moyo", RoleNames.Manager);
        var (administrator, _) = await CreateAsync("a.chirwa", "Anesu Chirwa", RoleNames.Administrator);

        return new Actors(officer, manager, administrator, officerId, managerId);
    }

    [Theory]
    [InlineData("USD", "850")]
    [InlineData("ZWG", "15000")]
    public async Task The_complete_payroll_journey_runs_end_to_end(string currency, string monthly)
    {
        var salary = decimal.Parse(monthly, System.Globalization.CultureInfo.InvariantCulture);

        var (db, companyId, permanentTypeId, _) = await SetUpAsync();
        using var _db = db;

        await ConfigureCompanyAsync(db, companyId);
        await VerifyEverythingAsync(db);

        // ---- The people who will run it -------------------------------------------------------
        var actors = await BuildFromCreatedUsersAsync(db);
        var officerId = actors.OfficerId;
        var managerId = actors.ManagerId;

        // ---- Employee, contract, recurring earning -----------------------------------------
        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);

        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        var contract = NewContract(companyId, employee.Id, permanentTypeId, salary, currency);
        await contracts.CreateInitialAsync(contract);

        db.Context.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile
        {
            EmployeeId = employee.Id, TaxNumber = "BP0001", NssaNumber = "NSSA0001"
        });

        var housingType = await db.Context.EarningTypes.AsNoTracking()
            .FirstAsync(t => t.CompanyId == companyId && t.Code != "BASIC");

        db.Context.EmployeeRecurringEarnings.Add(new Domain.Earnings.EmployeeRecurringEarning
        {
            EmployeeId = employee.Id,
            EarningTypeId = housingType.Id,
            Amount = 100m,
            CurrencyCode = currency,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        });

        // ---- Project and site ----------------------------------------------------------------
        var project = new Project
        {
            CompanyId = companyId, Code = "P-001", Name = "Nyanga shop", Status = ProjectStatus.Active
        };
        db.Context.Projects.Add(project);
        await db.Context.SaveChangesAsync();

        var site = new ProjectSite { ProjectId = project.Id, Code = "S-1", Name = "Main site" };
        db.Context.ProjectSites.Add(site);

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Live
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // ---- Time, overtime, leave, loan — captured by the officer ---------------------------
        var sheet = (await actors.Officer.Timesheets.CreateAsync(employee.Id, period.Id)).Value!;

        var entries = new List<TimeEntryRequest>();
        for (var date = period.StartDate; date <= period.EndDate; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            entries.Add(new TimeEntryRequest
            {
                WorkDate = date,
                OrdinaryHours = 8m,
                DaysWorked = 1m,
                ProjectId = project.Id,
                ProjectSiteId = site.Id,
                Overtime = date.Day == 3
                    ? new Dictionary<string, decimal> { ["OT_WEEKDAY"] = 4m }
                    : new Dictionary<string, decimal>()
            });
        }

        Assert.True((await actors.Officer.Timesheets.SetEntriesAsync(sheet.Id, entries)).IsValid);
        Assert.True((await actors.Officer.Timesheets.SubmitAsync(sheet.Id)).IsValid);

        var unpaidType = await db.Context.LeaveTypes.AsNoTracking().FirstAsync(t => t.Code == "UNPAID");
        var leave = (await actors.Officer.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = employee.Id,
            LeaveTypeId = unpaidType.Id,
            StartDate = new DateOnly(2026, 9, 21),
            EndDate = new DateOnly(2026, 9, 22)
        })).Value!;
        Assert.True((await actors.Officer.Leave.SubmitAsync(leave.Id)).IsValid);

        var loan = (await actors.Officer.Loans.CreateAsync(new LoanCommand
        {
            EmployeeId = employee.Id,
            CurrencyCode = currency,
            PrincipalAmount = 600m,
            InstalmentCount = 6,
            FirstInstalmentDate = period.PayDate
        })).Value!;
        Assert.True((await actors.Officer.Loans.SubmitAsync(loan.Id)).IsValid);

        // ---- Approval: a second person, in every case ---------------------------------------
        db.Context.ChangeTracker.Clear();
        Assert.True((await actors.Manager.Timesheets.ApproveAsync(sheet.Id)).IsValid);
        Assert.True((await actors.Manager.Leave.ApproveAsync(leave.Id)).IsValid);
        Assert.True((await actors.Manager.Loans.ApproveAsync(loan.Id)).IsValid);

        db.Context.ChangeTracker.Clear();
        Assert.True((await actors.Administrator.Loans
            .DisburseAsync(loan.Id, new DateOnly(2026, 9, 1), "FBC-RTGS-77012")).IsValid);

        // ---- Run, snapshot, calculate -------------------------------------------------------
        db.Context.ChangeTracker.Clear();
        var run = (await actors.Officer.Runs.CreateRunAsync(period.Id)).Value!;
        var calculated = await actors.Officer.Runs.CalculateAsync(run.Id);
        Assert.True(calculated.Succeeded, calculated.Validation.ToString());

        var runEmployee = await db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .Include(e => e.DeductionLines)
            .Include(e => e.EmployerCostLines)
            .Include(e => e.UnresolvedItems)
            .Include(e => e.CostAllocations)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        Assert.Empty(runEmployee.UnresolvedItems);
        Assert.True(runEmployee.IsCalculated);
        Assert.Equal(currency, runEmployee.CurrencyCode);

        // Every component reached the calculation: basic, the allowance, overtime, unpaid leave
        // and the loan.
        Assert.Contains(runEmployee.EarningLines, l => l.Code == "BASIC");
        Assert.Contains(runEmployee.EarningLines, l => l.Code == housingType.Code);
        Assert.Contains(runEmployee.EarningLines, l => l.Code == "OT_WEEKDAY");
        Assert.Contains(runEmployee.DeductionLines, l => l.Code == "UNPAID_LEAVE");
        Assert.Contains(runEmployee.DeductionLines, l => l.Code == "LOAN");
        Assert.Contains(runEmployee.DeductionLines, l => l.Code == "PAYE");
        Assert.Contains(runEmployee.DeductionLines, l => l.Code == "AIDSLEVY");
        Assert.Contains(runEmployee.EmployerCostLines, l => l.Code == "NSSA_POBS_ER");

        // ---- Explain: every figure is traceable ---------------------------------------------
        var trace = await db.Context.PayrollCalculationTraces.AsNoTracking()
            .Where(t => t.PayrollRunEmployeeId == runEmployee.Id)
            .ToListAsync();

        Assert.NotEmpty(trace);
        Assert.Contains(trace, t => t.ItemKey == "PAYE" && t.RuleId != null &&
                                    t.VerificationStatus == "Verified");
        Assert.Contains(trace, t => t.Stage == "DeriveOvertime");

        // ---- Approve, finalise ---------------------------------------------------------------
        db.Context.ChangeTracker.Clear();
        var approval = await actors.Manager.Runs.ApproveAsync(run.Id);
        Assert.True(approval.IsValid, approval.ToString());

        db.Context.ChangeTracker.Clear();
        var finalisation = await actors.Officer.Runs.FinaliseAsync(run.Id);
        Assert.True(finalisation.IsValid, finalisation.ToString());

        var obligations = await db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Include(o => o.Lines)
            .Where(o => o.PayrollRunId == run.Id)
            .ToListAsync();

        Assert.NotEmpty(obligations);
        Assert.All(obligations, o => Assert.True(o.IsCalculated));
        Assert.All(obligations, o => Assert.False(o.IsApproved));
        Assert.All(obligations, o => Assert.False(o.IsPaid));

        // ---- Net wages paid says nothing about the authorities -------------------------------
        db.Context.ChangeTracker.Clear();
        Assert.True((await actors.Officer.Runs.MarkNetWagesPaidAsync(run.Id)).IsValid);

        db.Context.ChangeTracker.Clear();
        var stillOwed = await db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == run.Id)
            .ToListAsync();
        Assert.All(stillOwed, o => Assert.False(o.IsPaid));

        // ---- Statutory payment ----------------------------------------------------------------
        var paye = stillOwed.Single(o => o.ObligationType == StatutoryObligationType.Paye);
        Assert.True((await actors.Officer.Obligations.ApproveAsync(paye.Id)).IsValid);

        db.Context.ChangeTracker.Clear();
        var payment = await actors.Officer.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id,
            Amount = paye.CalculatedAmount,
            CurrencyCode = currency,
            PaymentDate = new DateOnly(2026, 10, 8),
            PaymentReference = "FBC-RTGS-90114",
            AuthorityReceiptNumber = "ZIMRA-REC-55231"
        });
        Assert.True(payment.Succeeded, payment.Validation.ToString());

        // ---- Payslip ---------------------------------------------------------------------------
        db.Context.ChangeTracker.Clear();
        var payslip = await actors.Officer.Payslips.BuildAsync(runEmployee.Id);
        Assert.NotNull(payslip);
        Assert.Equal(currency, payslip!.Currency.Value);
        Assert.Equal(runEmployee.NetPayAmount, payslip.NetPay.Value?.Amount);
        Assert.False(payslip.IsDevelopmentCopy);

        // ---- Reports -----------------------------------------------------------------------------
        var reports = await actors.Officer.Reports.BuildAsync(run.Id);
        Assert.NotNull(reports);
        Assert.Single(reports!.CurrencySummary);
        Assert.Equal(currency, reports.CurrencySummary[0].CurrencyCode);
        Assert.Contains(reports.Inputs, i => i.InputType == "Timesheet");
        Assert.Contains(reports.StatutoryPayments.SelectMany(p => p.Rows),
            p => p.PaymentReference == "FBC-RTGS-90114");

        // ---- Accounting journal ------------------------------------------------------------------
        foreach (var type in Enum.GetValues<Tawaka.Domain.Accounting.GlMappingType>())
        {
            var mapping = await actors.Administrator.Journals.SetMappingAsync(
                companyId, type, currency, $"{(int)type + 2000}", type.ToString(), null);
            Assert.True(mapping.IsValid, mapping.ToString());
        }

        db.Context.ChangeTracker.Clear();
        var journals = await actors.Officer.Journals.BuildAsync(run.Id);

        // One journal, for the one currency this payroll is in, and it balances on its own.
        var journal = Assert.Single(journals);
        Assert.Equal(currency, journal.CurrencyCode);
        Assert.True(journal.IsBalanced,
            $"Journal out by {journal.TotalDebits - journal.TotalCredits} {currency}.");
        Assert.True(journal.TotalDebits > 0m);
        Assert.Empty(journal.Unmapped);

        // ---- Lock, then prove it is locked ---------------------------------------------------
        db.Context.ChangeTracker.Clear();
        Assert.True((await actors.Administrator.Runs.LockAsync(run.Id)).IsValid);
        db.Context.ChangeTracker.Clear();

        var locked = await db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(PayrollRunStatus.Locked, locked.Status);

        locked.Notes = "Tampered";
        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
        db.Context.ChangeTracker.Clear();

        var lockedResult = await db.Context.PayrollRunEmployees.SingleAsync(e => e.Id == runEmployee.Id);
        lockedResult.NetPayAmount = 1m;
        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
        db.Context.ChangeTracker.Clear();

        var lockedLine = await db.Context.PayrollEarningLines
            .FirstAsync(l => l.PayrollRunEmployeeId == runEmployee.Id);
        lockedLine.Amount = 1m;
        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
        db.Context.ChangeTracker.Clear();

        // The timesheet the run consumed is frozen with it.
        var lockedSheet = await db.Context.Timesheets.SingleAsync(t => t.Id == sheet.Id);
        Assert.Equal(InputApprovalStatus.Locked, lockedSheet.ApprovalStatus);
        lockedSheet.Notes = "Tampered";
        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
        db.Context.ChangeTracker.Clear();

        // A statutory payment is a later event and remains possible after the lock.
        var aids = await db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .SingleAsync(o => o.PayrollRunId == run.Id &&
                              o.ObligationType == StatutoryObligationType.AidsLevy);
        Assert.True((await actors.Officer.Obligations.ApproveAsync(aids.Id)).IsValid);

        // ---- Audit: every stage left a record, and every record names somebody -----------------
        db.Context.ChangeTracker.Clear();
        var audit = await db.Context.AuditLogs.AsNoTracking().ToListAsync();

        Assert.NotEmpty(audit);
        Assert.All(audit, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.UserId));
            Assert.NotEqual(default, entry.OccurredAt);
            Assert.False(string.IsNullOrWhiteSpace(entry.EntityName));
        });

        foreach (var recorded in new[]
                 {
                     nameof(Employee), nameof(EmployeeContract), nameof(PayrollRun),
                     nameof(Timesheet), nameof(EmployeeLoan), nameof(StatutoryObligation),
                     nameof(StatutoryPayment)
                 })
        {
            Assert.Contains(audit, entry => entry.EntityName == recorded);
        }

        Assert.Contains(audit, a => a.EntityName == nameof(PayrollRun));

        // And the run itself names who did each thing — the two accounts created at the start,
        // not "SYSTEM" and not the same person twice.
        db.Context.ChangeTracker.Clear();
        var attributed = await db.Context.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);

        Assert.Equal(officerId, attributed.CalculatedBy);
        Assert.Equal(managerId, attributed.ApprovedBy);
        Assert.Equal(officerId, attributed.FinalisedBy);
        Assert.NotNull(attributed.LockedBy);
        Assert.NotEqual(attributed.CalculatedBy, attributed.ApprovedBy);

        Assert.NotNull(attributed.CalculatedAt);
        Assert.NotNull(attributed.ApprovedAt);
        Assert.NotNull(attributed.FinalisedAt);
        Assert.NotNull(attributed.PaidAt);
        Assert.NotNull(attributed.LockedAt);

        // ---- Backup, change something, restore, and check the payroll came back unchanged -------
        var backupRoot = Path.Combine(
            Path.GetTempPath(), $"tawaka-journey-backup-{Guid.NewGuid():N}");
        var backups = new Tawaka.Infrastructure.Administration.BackupService(
            db.Context, db.User, db.Clock);
        var databasePath = backups.DatabasePath()!;

        try
        {
            var backup = await backups.BackupAsync(backupRoot, $"After the {currency} payroll");
            Assert.True(backup.Succeeded, backup.Validation.ToString());

            // Something changes after the backup.
            db.Context.ChangeTracker.Clear();
            var afterBackup = NewEmployee(companyId, "EMP-AFTER-BACKUP");
            afterBackup.NationalId = "63-9090909 Z 90";
            db.Context.Employees.Add(afterBackup);
            await db.Context.SaveChangesAsync();

            var restore = await backups.RestoreAsync(backup.Value!.BackupPath, confirmed: true);
            Assert.True(restore.Succeeded, restore.Validation.ToString());

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await using var reopened = new PayrollDbContext(
                new DbContextOptionsBuilder<PayrollDbContext>()
                    .UseSqlite($"Data Source={databasePath}").Options);

            Assert.Empty(await reopened.Database.GetPendingMigrationsAsync());
            Assert.DoesNotContain(
                await reopened.Employees.AsNoTracking().Select(e => e.EmployeeNumber).ToListAsync(),
                number => number == "EMP-AFTER-BACKUP");

            var restoredResult = await reopened.PayrollRunEmployees.AsNoTracking()
                .SingleAsync(e => e.PayrollRunId == run.Id);

            Assert.Equal(runEmployee.GrossEarningsAmount, restoredResult.GrossEarningsAmount);
            Assert.Equal(runEmployee.NetPayAmount, restoredResult.NetPayAmount);
            Assert.Equal(runEmployee.PayeAfterCreditsAmount, restoredResult.PayeAfterCreditsAmount);
            Assert.Equal(
                PayrollRunStatus.Locked,
                (await reopened.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).Status);

            // The audit survived with it — every entry up to the moment of the backup, plus the
            // one the restore itself wrote to say the history had been recovered. The employee
            // added after the backup, and the entry recording the backup, are both correctly
            // absent: neither existed when the copy was taken.
            var restoredAudit = await reopened.AuditLogs.AsNoTracking().ToListAsync();

            Assert.Equal(audit.Count + 1, restoredAudit.Count);
            Assert.Single(restoredAudit, e => e.EntityName == "Restore");
            Assert.DoesNotContain(restoredAudit, e => e.EntityName == "Backup");
        }
        finally
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// A correction after the lock: a second run, isolated from the first, leaving history intact.
    /// </summary>
    [Fact]
    public async Task A_correction_run_after_a_lock_leaves_the_original_untouched()
    {
        var (db, companyId, permanentTypeId, _) = await SetUpAsync();
        using var _db = db;

        await ConfigureCompanyAsync(db, companyId);
        await VerifyEverythingAsync(db);
        var actors = Build(db);

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(
            NewContract(companyId, employee.Id, permanentTypeId, 850m));

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Live
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var original = (await actors.Officer.Runs.CreateRunAsync(period.Id)).Value!;
        await actors.Officer.Runs.CalculateAsync(original.Id);
        db.Context.ChangeTracker.Clear();
        await actors.Manager.Runs.ApproveAsync(original.Id);
        db.Context.ChangeTracker.Clear();
        await actors.Officer.Runs.FinaliseAsync(original.Id);
        db.Context.ChangeTracker.Clear();
        await actors.Officer.Runs.MarkNetWagesPaidAsync(original.Id);
        db.Context.ChangeTracker.Clear();
        await actors.Administrator.Runs.LockAsync(original.Id);
        db.Context.ChangeTracker.Clear();

        var originalFigures = await db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == original.Id);
        var originalObligations = await db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == original.Id)
            .Select(o => new { o.ObligationType, o.CalculatedAmount })
            .ToListAsync();

        // The salary was wrong. A correction run is raised; history is not edited.
        var increased = NewContract(companyId, employee.Id, permanentTypeId, 900m);
        var superseded = await contracts.SupersedeAsync(
            employee.Id, increased, new DateOnly(2026, 9, 1), "Backdated increase agreed.");
        Assert.True(superseded.Succeeded, superseded.Validation.ToString());
        db.Context.ChangeTracker.Clear();

        var correction = (await actors.Officer.Runs
            .CreateRunAsync(period.Id, PayrollRunType.Correction)).Value!;
        var calculated = await actors.Officer.Runs.CalculateAsync(correction.Id);
        Assert.True(calculated.Succeeded, calculated.Validation.ToString());
        db.Context.ChangeTracker.Clear();

        var correctionFigures = await db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == correction.Id);

        // The correction reflects the new salary; the original still shows what was paid.
        Assert.NotEqual(originalFigures.GrossEarningsAmount, correctionFigures.GrossEarningsAmount);

        var untouched = await db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == original.Id);
        Assert.Equal(originalFigures.GrossEarningsAmount, untouched.GrossEarningsAmount);
        Assert.Equal(originalFigures.NetPayAmount, untouched.NetPayAmount);

        var obligationsAfter = await db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == original.Id)
            .Select(o => new { o.ObligationType, o.CalculatedAmount })
            .ToListAsync();

        Assert.Equal(originalObligations.Count, obligationsAfter.Count);
        Assert.All(obligationsAfter, after => Assert.Contains(originalObligations,
            before => before.ObligationType == after.ObligationType &&
                      before.CalculatedAmount == after.CalculatedAmount));
    }

    /// <summary>
    /// With rules unverified — the state every new installation starts in — payroll calculates for
    /// inspection but cannot be approved, and nothing downstream can happen.
    /// </summary>
    [Fact]
    public async Task With_unverified_rules_the_journey_stops_at_approval()
    {
        var (db, companyId, permanentTypeId, _) = await SetUpAsync();
        using var _db = db;

        await ConfigureCompanyAsync(db, companyId);

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(
            NewContract(companyId, employee.Id, permanentTypeId, 850m));

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var actors = Build(db);
        var run = (await actors.Officer.Runs.CreateRunAsync(period.Id)).Value!;
        await actors.Officer.Runs.CalculateAsync(run.Id);
        db.Context.ChangeTracker.Clear();

        var approval = await actors.Manager.Runs.ApproveAsync(run.Id);
        Assert.False(approval.IsValid);
        Assert.Contains(approval.Errors, e => e.Message.Contains("development calculation"));

        // And the release stage reports it honestly rather than looking ready.
        var readiness = await actors.Officer.Readiness.EvaluateAsync();
        Assert.Equal(ReleaseStage.ComplianceUnverified, readiness.Stage);
        Assert.Contains(readiness.ComplianceBlockers, b => b.Area == "Statutory rules");

        // Live payroll cannot be switched on to get around it.
        var enable = await actors.Administrator.Readiness
            .SetLivePayrollAsync(true, "We are behind on payroll.");
        Assert.False(enable.IsValid);
    }

    [Fact]
    public async Task A_mixed_currency_employee_is_refused_rather_than_guessed()
    {
        var (db, companyId, permanentTypeId, _) = await SetUpAsync();
        using var _db = db;

        await ConfigureCompanyAsync(db, companyId);
        await VerifyEverythingAsync(db);

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(
            NewContract(companyId, employee.Id, permanentTypeId, 850m));

        // An allowance in the other currency. The methodology for taxing this is Q1, still open.
        var allowanceType = await db.Context.EarningTypes.AsNoTracking()
            .FirstAsync(t => t.CompanyId == companyId && t.Code != "BASIC");

        db.Context.EmployeeRecurringEarnings.Add(new Domain.Earnings.EmployeeRecurringEarning
        {
            EmployeeId = employee.Id,
            EarningTypeId = allowanceType.Id,
            Amount = 5000m,
            CurrencyCode = "ZWG",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        });

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Live
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var actors = Build(db);
        var run = (await actors.Officer.Runs.CreateRunAsync(period.Id)).Value!;
        await actors.Officer.Runs.CalculateAsync(run.Id);

        var result = await db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        Assert.Contains(result.UnresolvedItems, u => u.ComplianceQuestion == "Q1");
        Assert.Null(result.GrossEarningsAmount);

        // And an unresolved figure blocks approval rather than being taken as zero.
        db.Context.ChangeTracker.Clear();
        var approval = await actors.Manager.Runs.ApproveAsync(run.Id);
        Assert.False(approval.IsValid);
    }

    // ---- Shared setup ------------------------------------------------------------------------

    private static async Task ConfigureCompanyAsync(TestDatabase db, Guid companyId)
    {
        var company = await db.Context.Companies.SingleAsync(c => c.Id == companyId);
        company.LegalName = "Tawaka Construction (Private) Limited";
        company.TaxNumber = "BP1234567";
        company.NssaEmployerNumber = "NSSA-0099";
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Marks every rule and treatment verified, as a real verification exercise would. This is
    /// test scaffolding for the journey, not a claim that the seeded figures are correct.
    /// </summary>
    private static async Task VerifyEverythingAsync(TestDatabase db)
    {
        foreach (var rule in await db.Context.StatutoryRules.ToListAsync())
        {
            if (rule.VerificationStatus != VerificationStatus.Disabled)
            {
                rule.VerificationStatus = VerificationStatus.Verified;
            }
        }

        foreach (var type in await db.Context.EarningTypes.ToListAsync())
        {
            type.TreatmentVerificationStatus = VerificationStatus.Verified;
        }

        foreach (var nssa in await db.Context.NssaRules.ToListAsync())
        {
            nssa.CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength;
        }

        // The seed ships a ZiG *annual* table and a USD NSSA rule only, which is exactly the
        // real position: whether ZIMRA publishes a ZWG monthly table is compliance question Q29,
        // still open. These rows are test scaffolding so the ZiG journey can be exercised
        // end to end; they are not a finding about what ZIMRA publishes, and none of these figures
        // is claimed to be correct.
        if (!await db.Context.TaxRules.AnyAsync(r =>
                r.Currency == "ZWG" && r.PeriodBasis == PeriodBasis.Monthly))
        {
            var zwgMonthly = new TaxRule
            {
                RuleId = "TEST-PAYE-ZWG-2026-MONTHLY",
                Name = "Test scaffolding: ZiG monthly table",
                Currency = "ZWG",
                TaxYear = 2026,
                PeriodBasis = PeriodBasis.Monthly,
                CalculationMethod = CalculationMethod.PeriodTable,
                BracketApplication = TaxBracketApplication.ProgressiveLadder,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = new DateOnly(2026, 12, 31),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "Test scaffolding" }
            };

            zwgMonthly.Brackets.Add(new TaxBracket
            {
                Sequence = 1, LowerBound = 0m, UpperBound = 2800m, Rate = 0m
            });
            zwgMonthly.Brackets.Add(new TaxBracket
            {
                Sequence = 2, LowerBound = 2800m, UpperBound = 8400m, Rate = 0.20m
            });
            zwgMonthly.Brackets.Add(new TaxBracket
            {
                Sequence = 3, LowerBound = 8400m, UpperBound = 84000m, Rate = 0.25m
            });
            zwgMonthly.Brackets.Add(new TaxBracket
            {
                Sequence = 4, LowerBound = 84000m, UpperBound = null, Rate = 0.40m
            });

            db.Context.StatutoryRules.Add(zwgMonthly);
        }

        if (!await db.Context.NssaRules.AnyAsync(r => r.Currency == "ZWG"))
        {
            db.Context.StatutoryRules.Add(new NssaRule
            {
                RuleId = "TEST-NSSA-POBS-2026-ZWG",
                Name = "Test scaffolding: NSSA POBS (ZiG)",
                Currency = "ZWG",
                EmployeeRate = 0.045m,
                EmployerRate = 0.045m,
                CeilingAmount = 18000m,
                CeilingPeriodBasis = PeriodBasis.Monthly,
                CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength,
                EarningsBasis = NssaEarningsBasis.BasicOnly,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = new DateOnly(2026, 12, 31),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "Test scaffolding" }
            });
        }

        if (!await db.Context.TaxExemptionRules.AnyAsync(r => r.Currency == "ZWG"))
        {
            db.Context.StatutoryRules.Add(new TaxExemptionRule
            {
                RuleId = "TEST-EXEMPT-BONUS-2026-ZWG",
                Name = "Test scaffolding: bonus exemption (ZiG)",
                Currency = "ZWG",
                ExemptionType = TaxExemptionType.AnnualBonus,
                LimitAmount = 21000m,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = new DateOnly(2026, 12, 31),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "Test scaffolding" }
            });
        }

        foreach (var currency in new[] { "USD", "ZWG" })
        {
            if (!await db.Context.ApwcsRules.AnyAsync(r => r.Currency == currency))
            {
                db.Context.StatutoryRules.Add(new ApwcsRule
                {
                    RuleId = $"APWCS-2026-{currency}",
                    Name = $"APWCS assessed rate ({currency})",
                    Currency = currency,
                    IndustryClassification = "Construction",
                    IndustryCode = "CON",
                    Rate = 0.025m,
                    Base = ApwcsBase.BasicEarnings,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    EffectiveTo = new DateOnly(2026, 12, 31),
                    VerificationStatus = VerificationStatus.Verified,
                    Source = new RuleSource { Source = "Test scaffolding" }
                });
            }
        }

        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
