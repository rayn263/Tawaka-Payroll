namespace Tawaka.Domain.Common;

/// <summary>
/// The lifecycle every payroll-affecting input moves through.
/// <para>
/// One enum for timesheets, leave requests and loans deliberately: they are different kinds of
/// thing, but the question payroll asks of each is identical — <em>has a person with the authority
/// to do so approved this?</em> A single vocabulary means that question has one answer shape, and
/// the snapshot builder can refuse everything that is not <see cref="Approved"/> or
/// <see cref="Locked"/> in one place rather than three.
/// </para>
/// </summary>
public enum InputApprovalStatus
{
    /// <summary>Being captured. Not visible to payroll, and freely editable.</summary>
    Draft = 0,

    /// <summary>Submitted for approval. Editable only by returning it to the submitter.</summary>
    Submitted = 1,

    /// <summary>Approved by someone authorised. This is the first state payroll may consume.</summary>
    Approved = 2,

    /// <summary>Refused outright. Payroll never consumes it; the record is kept with its reason.</summary>
    Rejected = 3,

    /// <summary>Sent back for correction. Returns to the submitter as editable.</summary>
    Returned = 4,

    /// <summary>Consumed by a payroll run and frozen. Corrections require a new input.</summary>
    Locked = 5
}

/// <summary>
/// An input payroll may consume, carrying who moved it through the lifecycle and when.
/// <para>
/// Every transition records an actor. Without that, "approved" is an assertion nobody can stand
/// behind, and segregation of duties cannot be enforced at all.
/// </para>
/// </summary>
public interface IApprovableInput
{
    InputApprovalStatus ApprovalStatus { get; }

    string? SubmittedBy { get; }
    DateTimeOffset? SubmittedAt { get; }
    string? ApprovedBy { get; }
    DateTimeOffset? ApprovedAt { get; }
    string? DecisionReason { get; }

    /// <summary>
    /// True when payroll may read this input. Approved and Locked only: a Draft has not been
    /// checked by anybody, a Submitted one has not been approved, and a Rejected or Returned one
    /// has been actively refused.
    /// </summary>
    bool IsAvailableToPayroll { get; }
}

/// <summary>
/// The transitions the lifecycle allows, in one place so three services cannot each invent their
/// own rules about what may follow what.
/// </summary>
public static class InputApprovalTransitions
{
    public static bool CanSubmit(InputApprovalStatus status) =>
        status is InputApprovalStatus.Draft or InputApprovalStatus.Returned;

    public static bool CanDecide(InputApprovalStatus status) =>
        status is InputApprovalStatus.Submitted;

    /// <summary>
    /// An approved input can still be withdrawn from approval before payroll consumes it; once it
    /// is Locked it cannot, because a payroll run has already read it.
    /// </summary>
    public static bool CanReturn(InputApprovalStatus status) =>
        status is InputApprovalStatus.Submitted or InputApprovalStatus.Approved;

    public static bool CanEdit(InputApprovalStatus status) =>
        status is InputApprovalStatus.Draft or InputApprovalStatus.Returned;

    public static bool CanLock(InputApprovalStatus status) =>
        status is InputApprovalStatus.Approved;

    public static bool IsAvailableToPayroll(InputApprovalStatus status) =>
        status is InputApprovalStatus.Approved or InputApprovalStatus.Locked;
}
