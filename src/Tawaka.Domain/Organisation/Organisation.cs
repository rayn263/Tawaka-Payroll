using Tawaka.Domain.Common;

namespace Tawaka.Domain.Organisation;

/// <summary>A department or cost centre.</summary>
public class Department : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The employee who manages the department, where one is appointed.</summary>
    public Guid? ManagerEmployeeId { get; set; }

    public Guid? ParentDepartmentId { get; set; }
    public string? CostCentreCode { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A job title. Configurable rather than a fixed list, because a construction business and a
/// retailer need entirely different ones.
/// </summary>
public class JobTitle : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Grade { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A work location: an office, depot, town or standing site.</summary>
public class Location : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A client a project is carried out for.</summary>
public class Client : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public bool IsActive { get; set; } = true;
}
