namespace Tawaka.Domain.Security;

/// <summary>
/// Every permission code in the system, in one place so they are not scattered as string literals.
/// </summary>
public static class Permissions
{
    public const string EmployeesView = "Employees.View";
    public const string EmployeesEdit = "Employees.Edit";
    public const string EmployeesViewSalary = "Employees.ViewSalary";
    public const string EmployeesEditSalary = "Employees.EditSalary";
    public const string EmployeesDeactivate = "Employees.Deactivate";

    public const string CompanyView = "Company.View";
    public const string CompanyEdit = "Company.Edit";

    public const string ProjectsView = "Projects.View";
    public const string ProjectsEdit = "Projects.Edit";

    public const string PayrollView = "Payroll.View";
    public const string PayrollCreate = "Payroll.Create";
    public const string PayrollCalculate = "Payroll.Calculate";

    /// <summary>Conflicts with <see cref="PayrollCalculate"/> under segregation of duties.</summary>
    public const string PayrollApprove = "Payroll.Approve";

    public const string PayrollFinalise = "Payroll.Finalise";
    public const string PayrollLock = "Payroll.Lock";
    public const string PayrollReopen = "Payroll.Reopen";
    public const string PayrollRecordPayment = "Payroll.RecordPayment";

    public const string StatutoryView = "Statutory.View";
    public const string StatutoryRecordPayment = "Statutory.RecordPayment";
    public const string StatutoryEditRules = "Statutory.EditRules";
    public const string StatutoryVerifyRules = "Statutory.VerifyRules";

    public const string ReportsRun = "Reports.Run";
    public const string ReportsExport = "Reports.Export";

    public const string UsersManage = "Users.Manage";
    public const string AuditView = "Audit.View";
    public const string SettingsEdit = "Settings.Edit";

    public static IReadOnlyList<(string Code, string Category, string Description, string? ConflictsWith)> All =>
        new List<(string, string, string, string?)>
        {
            (EmployeesView, "Employees", "View employee records", null),
            (EmployeesEdit, "Employees", "Create and edit employee records", null),
            (EmployeesViewSalary, "Employees", "View salary and rates", null),
            (EmployeesEditSalary, "Employees", "Change salary and rates", null),
            (EmployeesDeactivate, "Employees", "Deactivate or terminate an employee", null),
            (CompanyView, "Company", "View company profile and settings", null),
            (CompanyEdit, "Company", "Edit company profile, currencies and bank accounts", null),
            (ProjectsView, "Projects", "View projects and sites", null),
            (ProjectsEdit, "Projects", "Create and edit projects and sites", null),
            (PayrollView, "Payroll", "View payroll runs and the calculation preview", null),
            (PayrollCreate, "Payroll", "Create a payroll run", null),
            (PayrollCalculate, "Payroll", "Calculate a payroll run", PayrollApprove),
            (PayrollApprove, "Payroll", "Approve a payroll run", PayrollCalculate),
            (PayrollFinalise, "Payroll", "Finalise an approved payroll run", null),
            (PayrollLock, "Payroll", "Lock a payroll run", null),
            (PayrollReopen, "Payroll", "Reopen a locked payroll run", null),
            (PayrollRecordPayment, "Payroll", "Record payment of net wages", null),
            (StatutoryView, "Statutory", "View statutory obligations and rules", null),
            (StatutoryRecordPayment, "Statutory", "Record payment of a statutory obligation", null),
            (StatutoryEditRules, "Statutory", "Create and edit statutory rules", null),
            (StatutoryVerifyRules, "Statutory", "Mark a statutory rule as verified", null),
            (ReportsRun, "Reports", "Run reports", null),
            (ReportsExport, "Reports", "Export report data", null),
            (UsersManage, "Security", "Manage users, roles and permissions", null),
            (AuditView, "Security", "View the audit trail", null),
            (SettingsEdit, "Settings", "Edit system settings", null)
        };
}

/// <summary>The four default roles.</summary>
public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string PayrollOfficer = "Payroll Officer";
    public const string Manager = "Manager";
    public const string Viewer = "Viewer";
}
