using Tawaka.Domain.Common;

namespace Tawaka.Domain.Employees;

public enum EmployeeStatus
{
    Active = 0,
    Inactive = 1,
    Suspended = 2,
    Terminated = 3
}

public enum Gender
{
    Unspecified = 0,
    Female = 1,
    Male = 2,
    Other = 3
}

/// <summary>
/// An employee. Records are never deleted: an employee who leaves is given a terminal status so
/// that their payroll history, payslips and statutory records stay intact and reproducible.
/// </summary>
public class Employee : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;

    /// <summary>Zimbabwean national identity number, e.g. 63-1234567 X 42.</summary>
    public string? NationalId { get; set; }

    public string? PassportNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public string? MaritalStatus { get; set; }

    public string? Nationality { get; set; } = "Zimbabwean";

    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? PhotoPath { get; set; }

    public DateOnly HireDate { get; set; }

    public DateOnly? TerminationDate { get; set; }

    public string? TerminationReason { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public string? Notes { get; set; }

    public ICollection<EmployeeContract> Contracts { get; set; } = new List<EmployeeContract>();

    public ICollection<EmployeePaymentAccount> PaymentAccounts { get; set; }
        = new List<EmployeePaymentAccount>();

    public EmployeeStatutoryProfile? StatutoryProfile { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(MiddleName)
        ? $"{FirstName} {LastName}".Trim()
        : $"{FirstName} {MiddleName} {LastName}".Trim();

    /// <summary>Included in a payroll run only while genuinely active.</summary>
    public bool IsPayrollEligible => Status == EmployeeStatus.Active;

    public int? AgeAt(DateOnly date)
    {
        if (DateOfBirth is null)
        {
            return null;
        }

        var age = date.Year - DateOfBirth.Value.Year;
        if (date < DateOfBirth.Value.AddYears(age))
        {
            age--;
        }

        return age;
    }
}

/// <summary>Statutory identifiers and entitlements held against an employee.</summary>
public class EmployeeStatutoryProfile : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>ZIMRA business partner / tax number.</summary>
    public string? TaxNumber { get; set; }

    public bool IsPayeExempt { get; set; }
    public string? PayeExemptionReason { get; set; }

    public string? NssaNumber { get; set; }

    /// <summary>Overrides the eligibility rule for this employee, with a reason. Audited.</summary>
    public bool? NssaEligibilityOverride { get; set; }
    public string? NssaEligibilityOverrideReason { get; set; }

    public bool IsElderlyCreditEligible { get; set; }
    public bool IsDisabledCreditEligible { get; set; }
    public bool IsBlindCreditEligible { get; set; }
    public bool MedicalAidCreditApplies { get; set; }

    public string? NecMembershipNumber { get; set; }
    public bool IsNecMember { get; set; }

    public string? PensionSchemeNumber { get; set; }
}

/// <summary>A record of every status change, so employment history is reconstructable.</summary>
public class EmployeeStatusHistory : Entity
{
    public Guid EmployeeId { get; set; }
    public EmployeeStatus FromStatus { get; set; }
    public EmployeeStatus ToStatus { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}

public class EmployeeNextOfKin : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Relationship { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsEmergencyContact { get; set; }
    public bool IsBeneficiary { get; set; }
}

public class EmployeeDocument : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}
