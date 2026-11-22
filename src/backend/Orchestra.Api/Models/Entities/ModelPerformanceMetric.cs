namespace Orchestra.Api.Models.Entities;

public partial class ModelPerformanceMetric
{
    public Guid MetricId { get; set; }
    public Guid ModelId { get; set; }
    public string TaskCategory { get; set; } = null!;
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public decimal SuccessRate { get; set; }
    public decimal AverageResponseTimeMs { get; set; }
    public decimal TotalProviderCost { get; set; }
    public decimal AverageCostPerRequest { get; set; }
    public string Currency { get; set; } = null!;
    public DateTime MeasurementWindowStart { get; set; }
    public DateTime MeasurementWindowEnd { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    public virtual AiModel Model { get; set; } = null!;
}
