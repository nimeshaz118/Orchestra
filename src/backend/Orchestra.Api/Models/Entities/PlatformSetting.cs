using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class PlatformSetting
{
    public string SettingKey { get; set; } = null!;

    public string SettingValue { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsSensitive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public virtual AppUser? UpdatedByUser { get; set; }
}
