using System;
using System.Collections.Generic;

namespace Orchestra.Api.Models.Entities;

public partial class Organization
{
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<ApprovedEmailDomain> ApprovedEmailDomain { get; set; } = new List<ApprovedEmailDomain>();

    public virtual ICollection<CreditAccount> CreditAccount { get; set; } = new List<CreditAccount>();

    public virtual ICollection<Department> Department { get; set; } = new List<Department>();

    public virtual ICollection<QuotaPolicy> QuotaPolicy { get; set; } = new List<QuotaPolicy>();
}
