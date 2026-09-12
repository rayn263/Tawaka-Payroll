using Tawaka.Domain.Common;

namespace Tawaka.Domain.Organisation;

public enum ProjectStatus
{
    Planned = 0,
    Active = 1,
    OnHold = 2,
    Completed = 3,
    Cancelled = 4
}

/// <summary>
/// A project or contract that labour is costed against. Central to a contracting business: payroll
/// cost has to roll up to the job, not just to the department.
/// </summary>
public class Project : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }

    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;

    /// <summary>Contract value, stored with its currency; never converted on capture.</summary>
    public decimal? ContractValueAmount { get; set; }
    public string? ContractValueCurrency { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ProjectSite> Sites { get; set; } = new List<ProjectSite>();
}

/// <summary>A physical site belonging to a project. One project may run several sites.</summary>
public class ProjectSite : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }

    public Guid? LocationId { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;

    public bool IsActive { get; set; } = true;
}
