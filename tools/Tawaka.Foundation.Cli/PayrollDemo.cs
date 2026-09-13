using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Employees;
using Tawaka.Application.Payroll;
using Tawaka.Application.Security;
using Tawaka.Domain.Security;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
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
        await SignInAsync(auth, session, "tncube", OfficerPassword);

        var company = await db.Companies.FirstAsync();
        var permanent = await db.EmploymentTypes.FirstAsync(t => t.Code == "Permanent");

        if (verifyRules)
        {
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
                Mode = PayrollMode.Development
            };
            db.PayrollPeriods.Add(period);
            await db.SaveChangesAsync();
        }

        var run = (await runs.CreateRunAsync(period.Id)).Value!;
        await runs.CalculateAsync(run.Id);

        await PrintPreviewAsync(db, run.Id);
        await PrintExplanationAsync(db, run.Id, moyo);

        // The manager approves, not the officer who calculated it.
        await SignInAsync(auth, session, "rmoyo", ManagerPassword);
        await PrintApprovalAttemptAsync(runs, run.Id);
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

    private static async Task PrintApprovalAttemptAsync(PayrollRunService runs, Guid runId)
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
