namespace LotamSentinel.Domain.Entities;

/// <summary>Configurable technology master (MV, LV, DASA, SAS, STATCOM, HVDC,
/// DC, Renewable, Other). Adding one is a data change, not a deploy.</summary>
public class Technology
{
    public long Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<TechnologyMappingRule> MappingRules { get; set; } = new List<TechnologyMappingRule>();
    public ICollection<MaterialSectionTechnology> MaterialSections { get; set; } = new List<MaterialSectionTechnology>();
}

/// <summary>Deterministic rule used to classify a material section into a
/// technology, evaluated before any AI fallback.</summary>
public class TechnologyMappingRule
{
    public long Id { get; set; }
    public long TechnologyId { get; set; }
    public Technology Technology { get; set; } = null!;
    public MappingMatchType MatchType { get; set; }
    public string MatchValue { get; set; } = null!;
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>Resolved (material_section, technology) classification, tagged
/// with how it was produced. A material can map to more than one technology.</summary>
public class MaterialSectionTechnology
{
    public long Id { get; set; }
    public long MaterialSectionId { get; set; }
    public MaterialSection MaterialSection { get; set; } = null!;
    public long TechnologyId { get; set; }
    public Technology Technology { get; set; } = null!;
    public DerivationSource Source { get; set; }
    public decimal? Confidence { get; set; }
    public bool IsPrimary { get; set; } = true;
}
