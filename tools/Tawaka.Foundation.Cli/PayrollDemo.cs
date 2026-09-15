using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Employees;
using Tawaka.Application.Payroll;
using Tawaka.Application.Leave;
using Tawaka.Application.Loans;
using Tawaka.Application.Security;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Application.Time;
using Tawaka.Domain.Security;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Domain.Statutory.Obligations;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Foundation.Cli;

/// <summary>
/// Runs a real payroll end to end so the engine can be exercised without the Windows shell:
/// creates two employees in different currencies, a September 2026 period, calculates through the
/// engine and prints the preview and one full calculation explanation.
/// </summary>
public static class PayrollDemo
{
    private const string OfficerPassword = "Officer2026Site";
    private const string ManagerPassword = "Manager2026Site";
    private const string AdministratorPassword = "Admin2026Site";

    public static async Task RunAsync(IServiceProvider provider, bool verifyRules)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        var employees = scope.ServiceProvider.GetRequiredService<EmployeeService>();
        var contracts = scope.ServiceProvider.GetRequiredService<EmployeeContractService>();
        var runs = scope.ServiceProvider.GetRequiredService<PayrollRunService>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationService>();
        var session = scope.ServiceProvider.GetRequiredService<UserSession>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Payroll actions are attributed to a signed-in user, so the demonstration signs in as a
        // payroll officer to prepare the run and as a manager to attempt approval — which is how
        // segregation of duties actually plays out.
        await EnsureUserAsync(db, hasher, "tncube", "Tapiwa Ncube", RoleNames.PayrollOfficer, OfficerPassword);
        await EnsureUserAsync(db, hasher, "rmoyo", "Rudo Moyo", RoleNames.Manager, ManagerPassword);
        await EnsureUserAsync(db, hasher, "achirwa", "Anesu Chirwa", RoleNames.Administrator,
            AdministratorPassword);
        await SignInAsync(auth, session, "tncube", OfficerPassword);

        var company = await db.Companies.FirstAsync();
        var permanent = await db.EmploymentTypes.FirstAsync(t => t.Code == "Permanent");

        if (verifyRules)
        {
            Console.WriteLine();
            Console.WriteLine("*** --verify-rules: every seeded rule is being marked Verified for");
            Console.WriteLine("*** demonstration only, and this period runs in LIVE mode. No rule has");
            Console.WriteLine("*** actually been verified against an authoritative source. Never use");
            Console.WriteLine("*** this switch against a real database.");
            await VerifyRulesAsync(db);
        }

        var moyo = await EnsureEmployeeAsync(db, employees, contracts, company.Id, permanent.Id,
            "EMP-0031", "John", "Moyo", "63-1234567 X 42", 850m, "USD");
        await EnsureEmployeeAsync(db, employees, contracts, company.Id, permanent.Id,
            "EMP-0032", "Peter", "Dube", "63-7654321 A 11", 15000m, "ZWG");

        var period = await db.PayrollPeriods.FirstOrDefaultAsync(p => p.Code == "2026-09");
        if (period is null)
        {
            period = new PayrollPeriod
            {
                CompanyId = company.Id,
                Code = "2026-09",
                Name = "September 2026",
                Frequency = PeriodBasis.Monthly,
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 9, 30),
                PayDate = new DateOnly(2026, 9, 30),
                TaxYear = 2026,

                // --verify-rules is the demonstration switch: it pretends every seeded rule has
                // been verified against an authoritative source so the workflow after approval can
                // be exercised. The real gate is unchanged — without the switch this period stays
                // in Development and the run cannot be approved, which is what a real install sees
                // until a rule is verified against evidence.
                Mode = verifyRules ? PayrollMode.Live : PayrollMode.Development
            };
            db.PayrollPeriods.Add(period);
            await db.SaveChangesAsync();
        }

        // Milestone 5: approved inputs before the calculation reads them.
        await PrepareInputsAsync(scope.ServiceProvider, db, auth, session, company.Id, moyo, period);

        var run = (await runs.CreateRunAsync(period.Id)).Value!;
        await runs.CalculateAsync(run.Id);
        await PrintInputSourcesAsync(scope.ServiceProvider, run.Id);

        await PrintPreviewAsync(db, run.Id);
        await PrintExplanationAsync(db, run.Id, moyo);

        // The manager approves, not the officer who calculated it.
        await SignInAsync(auth, session, "rmoyo", ManagerPassword);
        var approved = await PrintApprovalAttemptAsync(runs, run.Id);

        // Finalising, and settling the authorities afterwards, is the officer's work again.
        await SignInAsync(auth, session, "tncube", OfficerPassword);
        await PrintObligationLifecycleAsync(scope.ServiceProvider, db, runs, company.Id, run.Id, approved);
    }

    /// <summary>
    /// Walks the statutory obligation lifecycle: finalising the run creates the obligations with
    /// Calculated and Deducted set, approval authorises payment, and only a recorded payment with a
    /// reference makes anything paid. Each step prints the register so the four states can be seen
    /// moving independently.
    /// </summary>
    private static async Task PrintObligationLifecycleAsync(
        IServiceProvider services, PayrollDbContext db, PayrollRunService runs,
        Guid companyId, Guid runId, bool approved)
    {
        var obligations = services.GetRequiredService<StatutoryObligationService>();

        Console.WriteLine();
        Console.WriteLine("STATUTORY OBLIGATIONS");
        Console.WriteLine(new string('-', 112));

        if (!approved)
        {
            Console.WriteLine("  The run was not approved, so there is nothing to finalise and no");
            Console.WriteLine("  obligation exists. Nothing is recorded as owed, and nothing is");
            Console.WriteLine("  recorded as paid.");
            Console.WriteLine();
            Console.WriteLine("  This is the live payroll gate working: a development calculation");
            Console.WriteLine("  cannot be approved, so it can never produce a statutory liability");
            Console.WriteLine("  that looks settled. Pass --verify-rules to simulate verified rules");
            Console.WriteLine("  and walk the rest of the lifecycle.");
            return;
        }

        var finalise = await runs.FinaliseAsync(runId);
        if (!finalise.IsValid)
        {
            foreach (var error in finalise.Errors)
            {
                Console.WriteLine($"  Finalisation refused: {error.Message}");
            }

            return;
        }

        Console.WriteLine("  Run finalised. Obligations created with Calculated and Deducted set.");
        db.ChangeTracker.Clear();
        await PrintRegisterAsync(obligations, companyId);

        var paye = (await obligations.GetRegisterAsync(companyId))
            .FirstOrDefault(o => o.ObligationType == StatutoryObligationType.Paye &&
                                 o.CurrencyCode == "USD");
        if (paye is null)
        {
            return;
        }

        var approval = await obligations.ApproveAsync(paye.Id);
        Console.WriteLine();
        Console.WriteLine(approval.IsValid
            ? "  USD PAYE approved for payment. Note it is still NOT paid."
            : $"  Approval refused: {string.Join("; ", approval.Errors.Select(e => e.Message))}");

        db.ChangeTracker.Clear();
        await PrintRegisterAsync(obligations, companyId);

        // A deliberately partial payment, to show that the outstanding balance is tracked rather
        // than the obligation flipping straight to paid.
        var part = Math.Round(paye.CalculatedAmount / 2m, 2);
        var payment = await obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id,
            Amount = part,
            CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8),
            PaymentReference = "FBC-RTGS-90114",
            PaymentMethod = StatutoryPaymentMethod.BankTransfer,
            AuthorityReceiptNumber = "ZIMRA-REC-55231"
        });

        Console.WriteLine();
        Console.WriteLine(payment.Succeeded
            ? $"  Part payment of USD {part:N2} recorded against USD PAYE (ref FBC-RTGS-90114)."
            : $"  Payment refused: {string.Join("; ", payment.Validation!.Errors.Select(e => e.Message))}");

        // The same payment without a reference, to show it is refused rather than accepted quietly.
        var unreferenced = await obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id,
            Amount = 10m,
            CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8),
            PaymentReference = string.Empty
        });

        if (!unreferenced.Succeeded)
        {
            foreach (var error in unreferenced.Validation!.Errors)
            {
                Console.WriteLine($"  Payment without a reference refused: {error.Message}");
            }
        }

        db.ChangeTracker.Clear();
        await PrintRegisterAsync(obligations, companyId);

        Console.WriteLine();
        Console.WriteLine("  The remaining balance is still outstanding. No step above marked an");
        Console.WriteLine("  obligation paid because payroll was approved: only a recorded payment");
        Console.WriteLine("  with a reference moves money to an authority.");
    }

    private static async Task PrintRegisterAsync(
        StatutoryObligationService obligations, Guid companyId)
    {
        var register = await obligations.GetRegisterAsync(companyId);
        var today = new DateOnly(2026, 10, 8);

        Console.WriteLine();
        foreach (var group in register.GroupBy(o => o.CurrencyCode).OrderBy(g => g.Key))
        {
            var label = group.Key == "ZWG" ? "ZiG" : group.Key;
            Console.WriteLine($"  {label} obligations");
            Console.WriteLine($"  {"OBLIGATION",-28}{"CALC",12}{"DEDUCTED",10}{"APPROVED",10}" +
                              $"{"PAID",12}{"OUTSTANDING",14}  STATUS");

            foreach (var obligation in group.OrderBy(o => o.ObligationType))
            {
                var deducted = !obligation.IsDeductionApplicable
                    ? "n/a"
                    : obligation.IsDeducted ? "yes" : "no";

                Console.WriteLine(
                    $"  {obligation.ObligationType,-28}{obligation.CalculatedAmount,12:N2}" +
                    $"{deducted,10}{(obligation.IsApproved ? "yes" : "no"),10}" +
                    $"{obligation.PaidAmount.Amount,12:N2}{obligation.Outstanding.Amount,14:N2}" +
                    $"  {obligation.StatusOn(today)}");
            }

            // Per currency, and only per currency: a USD liability and a ZiG liability are never
            // added together.
            Console.WriteLine(
                $"  {"TOTAL " + label,-28}{group.Sum(o => o.CalculatedAmount),12:N2}" +
                $"{string.Empty,10}{string.Empty,10}{group.Sum(o => o.PaidAmount.Amount),12:N2}" +
                $"{group.Sum(o => o.Outstanding.Amount),14:N2}");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Captures and approves a timesheet, a period of unpaid leave and a loan, so the run has real
    /// inputs to consume. The officer captures and the manager approves: a single person doing both
    /// is exactly what segregation of duties exists to prevent, and the services refuse it.
    /// </summary>
    private static async Task PrepareInputsAsync(
        IServiceProvider services, PayrollDbContext db, AuthenticationService auth,
        UserSession session, Guid companyId, Guid employeeId, PayrollPeriod period)
    {
        var timesheets = services.GetRequiredService<TimesheetService>();
        var leave = services.GetRequiredService<LeaveService>();
        var loans = services.GetRequiredService<LoanService>();

        if (await db.Timesheets.AnyAsync(t => t.PayrollPeriodId == period.Id))
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("PAYROLL INPUTS — capture and approval");
        Console.WriteLine(new string('-', 112));

        var sheet = (await timesheets.CreateAsync(employeeId, period.Id)).Value!;

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

                // Four hours of weekday overtime in the first week, to show it priced by its rule.
                Overtime = date.Day <= 4
                    ? new Dictionary<string, decimal> { ["OT_WEEKDAY"] = 1m }
                    : new Dictionary<string, decimal>()
            });
        }

        await timesheets.SetEntriesAsync(sheet.Id, entries);
        await timesheets.SubmitAsync(sheet.Id);
        Console.WriteLine($"  Timesheet submitted: {entries.Count} days, " +
                          $"{entries.Sum(e => e.Overtime.Values.Sum()):N2} overtime hours.");

        var unpaidType = await db.LeaveTypes.AsNoTracking().FirstAsync(l => l.Code == "UNPAID");
        var request = (await leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = employeeId,
            LeaveTypeId = unpaidType.Id,
            StartDate = period.StartDate.AddDays(20),
            EndDate = period.StartDate.AddDays(21),
            Reason = "Family matter"
        })).Value;

        if (request is not null)
        {
            await leave.SubmitAsync(request.Id);
            Console.WriteLine($"  Unpaid leave submitted: {request.Days:N2} day(s).");
        }

        var loan = (await loans.CreateAsync(new LoanCommand
        {
            EmployeeId = employeeId,
            CurrencyCode = "USD",
            PrincipalAmount = 600m,
            InstalmentCount = 6,
            FirstInstalmentDate = period.PayDate,
            Purpose = "School fees"
        })).Value!;
        await loans.SubmitAsync(loan.Id);
        Console.WriteLine($"  Loan submitted: {loan.LoanNumber}, USD {loan.PrincipalAmount:N2} " +
                          $"over {loan.InstalmentCount} instalments.");

        // The manager approves. The officer who captured all of this cannot.
        await SignInAsync(auth, session, "rmoyo", ManagerPassword);

        var refused = await timesheets.ApproveAsync(sheet.Id);
        Console.WriteLine(refused.IsValid
            ? "  Timesheet approved."
            : $"  (Manager approving) {string.Join("; ", refused.Errors.Select(e => e.Message))}");

        if (request is not null)
        {
            await leave.ApproveAsync(request.Id);
        }

        await loans.ApproveAsync(loan.Id);
        Console.WriteLine("  Leave and loan approved by the manager.");

        // Disbursement is a third pair of hands again: it is money leaving the business, so it
        // sits with the administrator rather than with either the captor or the approver.
        await SignInAsync(auth, session, "achirwa", AdministratorPassword);
        await loans.DisburseAsync(loan.Id, period.StartDate, "FBC-RTGS-77012");
        Console.WriteLine("  Loan disbursed. The first instalment is now recoverable.");

        await SignInAsync(auth, session, "tncube", OfficerPassword);

        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Prints exactly which approved records the run consumed — the question the stored snapshot
    /// and the input-source rows exist to answer.
    /// </summary>
    private static async Task PrintInputSourcesAsync(IServiceProvider services, Guid runId)
    {
        var store = services.GetRequiredService<PayrollSnapshotStore>();
        var sources = await store.GetInputSourcesAsync(runId);

        Console.WriteLine();
        Console.WriteLine("APPROVED INPUTS CONSUMED BY THIS RUN");
        Console.WriteLine(new string('-', 112));

        if (sources.Count == 0)
        {
            Console.WriteLine("  None.");
            return;
        }

        // One row is written per employee, so an input shared across the run — the calendar —
        // appears once per employee. Grouped here for readability; the rows themselves stay
        // per-employee, which is what makes "who consumed this" answerable.
        foreach (var group in sources
                     .GroupBy(s => new { s.InputType, s.InputId })
                     .OrderBy(g => g.Key.InputType))
        {
            var source = group.First();
            var approver = source.ApprovedBy is null
                ? "no approver recorded"
                : $"approved {source.ApprovedAt:dd MMM yyyy HH:mm}";
            var employees = group.Count() == 1 ? "1 employee" : $"{group.Count()} employees";
            Console.WriteLine(
                $"  {source.InputType,-16}{source.Description} ({approver}, {employees})");
        }

        Console.WriteLine();
        Console.WriteLine("  The snapshot these came from is stored and hashed, so this run still");
        Console.WriteLine("  reproduces against the inputs it actually had, however they move later.");
    }

    private static async Task EnsureUserAsync(
        PayrollDbContext db, IPasswordHasher hasher, string username, string fullName,
        string roleName, string password)
    {
        if (await db.Users.AnyAsync(u => u.Username == username))
        {
            return;
        }

        var user = new User
        {
            Username = username,
            FullName = fullName,
            PasswordHash = hasher.Hash(password),
            IsActive = true
        };
        db.Users.Add(user);

        var role = await db.Roles.FirstAsync(r => r.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    private static async Task SignInAsync(
        AuthenticationService auth, UserSession session, string username, string password)
    {
        var result = await auth.AuthenticateAsync(username, password, "CLI");
        if (result.User is null)
        {
            throw new InvalidOperationException($"Could not sign in as {username}: {result.Message}");
        }

        session.SignIn(result.User);
        Console.WriteLine();
        Console.WriteLine($"Signed in as {result.User.FullName} ({string.Join(", ", result.User.Roles)})");
    }

    private static async Task<Guid> EnsureEmployeeAsync(
        PayrollDbContext db, EmployeeService employees, EmployeeContractService contracts,
        Guid companyId, Guid employmentTypeId, string number, string firstName, string lastName,
        string nationalId, decimal salary, string currency)
    {
        var existing = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeNumber == number);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await employees.CreateAsync(new Employee
        {
            CompanyId = companyId,
            EmployeeNumber = number,
            FirstName = firstName,
            LastName = lastName,
            NationalId = nationalId,
            DateOfBirth = new DateOnly(1988, 4, 2),
            HireDate = new DateOnly(2021, 3, 4),
            Status = EmployeeStatus.Active
        });

        var employeeId = created.Value!.Id;

        db.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile
        {
            EmployeeId = employeeId, TaxNumber = "BP" + number, NssaNumber = "N" + number
        });
        await db.SaveChangesAsync();

        await contracts.CreateInitialAsync(new EmployeeContract
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            EmploymentTypeId = employmentTypeId,
            StartDate = new DateOnly(2021, 3, 4),
            PayrollCurrency = currency,
            PaymentFrequency = PaymentFrequency.Monthly,
            EarningsBasis = EarningsBasis.MonthlySalary,
            MonthlyRate = salary,
            StandardHoursPerDay = 8m,
            StandardDaysPerWeek = 5m
        });

        return employeeId;
    }

    /// <summary>
    /// Marks the seeded rules verified, as a real verification exercise would, so the engine can
    /// be seen calculating. This is a demonstration switch only — it does not make the rules
    /// correct, and the application never does this by itself.
    /// </summary>
    private static async Task VerifyRulesAsync(PayrollDbContext db)
    {
        foreach (var rule in await db.StatutoryRules.ToListAsync())
        {
            if (rule.VerificationStatus != VerificationStatus.Disabled)
            {
                rule.VerificationStatus = VerificationStatus.Verified;
            }
        }

        foreach (var type in await db.EarningTypes.ToListAsync())
        {
            type.TreatmentVerificationStatus = VerificationStatus.Verified;
        }

        foreach (var nssa in await db.NssaRules.ToListAsync())
        {
            nssa.CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength;
        }

        if (!await db.ApwcsRules.AnyAsync())
        {
            foreach (var currency in new[] { "USD", "ZWG" })
            {
                db.ApwcsRules.Add(new ApwcsRule
                {
                    RuleId = $"APWCS-2026-{currency}",
                    Name = $"APWCS assessed rate ({currency})",
                    Currency = currency,
                    IndustryClassification = "Construction",
                    IndustryCode = "CON",
                    Rate = 0.025m,
                    Base = ApwcsBase.BasicEarnings,
                    CalculationMethod = CalculationMethod.PercentageOfBase,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    VerificationStatus = VerificationStatus.Verified,
                    Source = new RuleSource { Source = "NSSA assessment (demonstration value)" }
                });
            }
        }

        if (!await db.NssaRules.AnyAsync(r => r.Currency == "ZWG"))
        {
            db.NssaRules.Add(new NssaRule
            {
                RuleId = "NSSA-POBS-2026-ZWG", Name = "NSSA POBS 2026 (ZiG)", Currency = "ZWG",
                EmployeeRate = 0.045m, EmployerRate = 0.045m, CeilingAmount = 18000m,
                CeilingPeriodBasis = PeriodBasis.Monthly,
                CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength,
                EarningsBasis = NssaEarningsBasis.BasicOnly, MinimumAge = 16, MaximumAge = 64,
                MinimumDaysInMonth = 18, CalculationMethod = CalculationMethod.CappedPercentage,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "Demonstration value — ZiG ceiling unverified" }
            });
        }

        if (!await db.TaxRules.AnyAsync(r => r.Currency == "ZWG" && r.PeriodBasis == PeriodBasis.Monthly))
        {
            var table = new TaxRule
            {
                RuleId = "PAYE-ZWG-2026-MONTHLY", Name = "PAYE ZiG monthly 2026", Currency = "ZWG",
                TaxYear = 2026, PeriodBasis = PeriodBasis.Monthly,
                CalculationMethod = CalculationMethod.PeriodTable,
                EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "Demonstration value — derived, not official" }
            };
            table.Brackets.Add(new TaxBracket { Sequence = 1, LowerBound = 0m, UpperBound = 2800m, Rate = 0m });
            table.Brackets.Add(new TaxBracket { Sequence = 2, LowerBound = 2800m, UpperBound = 8400m, Rate = 0.20m });
            table.Brackets.Add(new TaxBracket { Sequence = 3, LowerBound = 8400m, UpperBound = 84000m, Rate = 0.25m });
            table.Brackets.Add(new TaxBracket { Sequence = 4, LowerBound = 84000m, UpperBound = null, Rate = 0.40m });
            db.TaxRules.Add(table);
        }

        await db.SaveChangesAsync();
    }

    private static async Task PrintPreviewAsync(PayrollDbContext db, Guid runId)
    {
        var rows = await db.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems)
            .Where(e => e.PayrollRunId == runId)
            .OrderBy(e => e.EmployeeName)
            .ToListAsync();

        Console.WriteLine();
        Console.WriteLine("PAYROLL PREVIEW — September 2026");
        Console.WriteLine(new string('-', 112));

        foreach (var group in rows.GroupBy(r => r.CurrencyCode).OrderBy(g => g.Key))
        {
            var label = group.Key == "ZWG" ? "ZiG" : group.Key;
            Console.WriteLine($"{label} payroll");
            Console.WriteLine($"{"EMPLOYEE",-22}{"GROSS",12}{"TAXABLE",12}{"PAYE",12}" +
                              $"{"AIDS LEVY",12}{"NSSA",10}{"NET PAY",12}{"EMPR COST",12}");

            foreach (var row in group)
            {
                Console.WriteLine(
                    $"{row.EmployeeName,-22}{Show(row.GrossEarningsAmount),12}" +
                    $"{Show(row.TaxableIncomeAmount),12}{Show(row.PayeAfterCreditsAmount),12}" +
                    $"{Show(row.AidsLevyAmount),12}{Show(row.NssaEmployeeAmount),10}" +
                    $"{Show(row.NetPayAmount),12}{Show(row.TotalEmployerCostAmount),12}");
            }

            // Totals are per currency. USD and ZiG are never added together.
            Console.WriteLine(
                $"{"TOTAL " + label,-22}{Total(group, r => r.GrossEarningsAmount),12}" +
                $"{Total(group, r => r.TaxableIncomeAmount),12}" +
                $"{Total(group, r => r.PayeAfterCreditsAmount),12}" +
                $"{Total(group, r => r.AidsLevyAmount),12}" +
                $"{Total(group, r => r.NssaEmployeeAmount),10}" +
                $"{Total(group, r => r.NetPayAmount),12}" +
                $"{Total(group, r => r.TotalEmployerCostAmount),12}");
            Console.WriteLine();
        }

        var unresolved = rows.SelectMany(r => r.UnresolvedItems).ToList();
        if (unresolved.Count > 0)
        {
            Console.WriteLine("LIVE PAYROLL BLOCKED — STATUTORY RULE VERIFICATION REQUIRED");
            foreach (var group in unresolved.GroupBy(u => u.Code))
            {
                var item = group.First();
                Console.WriteLine($"  {item.Code}" +
                                  (item.ComplianceQuestion is null ? string.Empty : $" ({item.ComplianceQuestion})"));
                Console.WriteLine($"    {item.Message}");
                Console.WriteLine($"    To clear: {item.Remedy}");
            }

            Console.WriteLine();
            Console.WriteLine("  Note: these figures show as '-', not 0.00. A missing rule is not a");
            Console.WriteLine("  zero deduction.");
            Console.WriteLine();
        }
    }

    private static async Task PrintExplanationAsync(PayrollDbContext db, Guid runId, Guid employeeId)
    {
        var runEmployee = await db.PayrollRunEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.PayrollRunId == runId && e.EmployeeId == employeeId);
        if (runEmployee is null)
        {
            return;
        }

        var trace = await db.PayrollCalculationTraces.AsNoTracking()
            .Where(t => t.PayrollRunEmployeeId == runEmployee.Id)
            .OrderBy(t => t.Sequence)
            .ToListAsync();

        Console.WriteLine($"EXPLAIN CALCULATION — {runEmployee.EmployeeName} ({runEmployee.CurrencyCode})");
        Console.WriteLine(new string('-', 112));

        foreach (var entry in trace)
        {
            var value = entry.OutputAmount is null
                ? string.Empty
                : $"{entry.OutputCurrency} {entry.OutputAmount.Value:N2}";
            Console.WriteLine($"{entry.ItemKey,-32}{value,20}");

            if (!string.IsNullOrWhiteSpace(entry.RuleId))
            {
                Console.WriteLine($"    Rule: {entry.RuleId} [{entry.VerificationStatus}]" +
                                  $" effective {entry.RuleEffectiveFrom:yyyy-MM-dd}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Steps))
            {
                foreach (var step in System.Text.Json.JsonSerializer
                             .Deserialize<List<string>>(entry.Steps) ?? new List<string>())
                {
                    Console.WriteLine($"    {step}");
                }
            }

            if (entry.RawValue is not null && !string.IsNullOrWhiteSpace(entry.RoundingApplied))
            {
                Console.WriteLine($"    Unrounded {entry.RawValue.Value:N4}, rounded {entry.RoundingApplied}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Explanation))
            {
                Console.WriteLine($"    {entry.Explanation}");
            }

            Console.WriteLine();
        }
    }

    private static async Task<bool> PrintApprovalAttemptAsync(PayrollRunService runs, Guid runId)
    {
        var approval = await runs.ApproveAsync(runId);
        Console.WriteLine("APPROVAL ATTEMPT");
        Console.WriteLine(new string('-', 112));
        if (approval.IsValid)
        {
            Console.WriteLine("  Approved.");
        }
        else
        {
            foreach (var error in approval.Errors)
            {
                Console.WriteLine($"  Refused: {error.Message}");
            }
        }

        return approval.IsValid;
    }

    private static string Show(decimal? amount) =>
        amount is null ? "-" : amount.Value.ToString("N2");

    private static string Total(
        IEnumerable<Domain.Payroll.PayrollRunEmployee> rows,
        Func<Domain.Payroll.PayrollRunEmployee, decimal?> selector)
    {
        var values = rows.Select(selector).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? "-" : values.Sum().ToString("N2");
    }
}
