using Tawaka.Application.Calendars;
using Tawaka.Application.Leave;
using Tawaka.Application.Loans;
using Tawaka.Application.Payroll;
using Tawaka.Application.Statutory;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Application.Time;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Builds the payroll service graph against a test database.
/// <para>
/// One place, so that adding a dependency to <see cref="PayrollRunService"/> does not send a
/// compile error through every test fixture that happens to construct one.
/// </para>
/// </summary>
public sealed record PayrollServices(
    PayrollSnapshotBuilder Snapshots,
    PayrollSnapshotStore SnapshotStore,
    PayrollRunService Runs,
    StatutoryObligationService Obligations,
    TimesheetService Timesheets,
    LeaveService Leave,
    HolidayCalendarService Calendars,
    LoanService Loans)
{
    public static PayrollServices For(TestDatabase db) => For(db, db.User);

    /// <summary>
    /// The same graph acting as a different user, so segregation of duties can be exercised: one
    /// identity submits, another approves, against the same database.
    /// </summary>
    public static PayrollServices For(TestDatabase db, Tawaka.Application.Abstractions.ICurrentUser user)
    {
        var resolver = new StatutoryRuleResolver(new EfStatutoryRuleSource(db.Context));

        var timesheets = new TimesheetService(db.Context, user, db.Clock);
        var leave = new LeaveService(db.Context, user, db.Clock);
        var calendars = new HolidayCalendarService(db.Context, user);
        var loans = new LoanService(db.Context, user, db.Clock);

        var snapshots = new PayrollSnapshotBuilder(db.Context, resolver, loans);
        var snapshotStore = new PayrollSnapshotStore(db.Context, db.Clock);
        var obligations = new StatutoryObligationService(db.Context, user, db.Clock);

        var runs = new PayrollRunService(
            db.Context, snapshots, snapshotStore, obligations, timesheets, loans,
            user, db.Clock);

        return new PayrollServices(
            snapshots, snapshotStore, runs, obligations, timesheets, leave, calendars, loans);
    }
}
