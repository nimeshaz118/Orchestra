namespace Orchestra.Api.Models.Entities;

public partial class RoutingPolicy
{
    public Guid PolicyId { get; set; }
    public string PolicyName { get; set; } = null!;
    public string? Description { get; set; }
    public Guid ModelId { get; set; }
    public string TaskCategory { get; set; } = null!;
    public string ScopeType { get; set; } = null!;
    public Guid? DepartmentId { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual AiModel Model { get; set; } = null!;
    public virtual Department? Department { get; set; }
    public virtual AppUser CreatedByUser { get; set; } = null!;
}
