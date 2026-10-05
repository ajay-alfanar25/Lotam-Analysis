using LotamSentinel.Application.Comparison;
using LotamSentinel.Domain.Entities;
using Xunit;

namespace LotamSentinel.Tests;

public class ComparisonEngineTests
{
    private static EntrySnapshot Snap(
        long entryId, long supplierId, string name, string code, long sectionId = 1,
        ApprovalStatus status = ApprovalStatus.Qualified,
        string? modelRating = "MODEL-A", string? remarks = "PQ Letter, standard conditions.",
        string? localRep = null, string? country = "SAUDI ARABIA")
        => new(entryId, supplierId, name, code, sectionId, status,
            modelRating, modelRating, remarks, remarks, localRep, country, country, 1);

    [Fact]
    public void RealWorldCase_Statcom52_2_FourNewSuppliers()
    {
        // Verified directly against the two supplied PDFs (Phase A/B):
        // v1 has NR Electric + Hyosung; v2 adds Rongxin Huiko, NARI Technology,
        // Hitachi Energy, and Grid Solutions Oy.
        var previous = new List<EntrySnapshot>
        {
            Snap(1, 101, "NR Electric Co., Ltd.", "52.2"),
            Snap(2, 102, "HYOSUNG HEAVY INDUSTRIES CORPORATION (HICO)", "52.2"),
        };

        var current = new List<EntrySnapshot>
        {
            Snap(11, 101, "NR Electric Co., Ltd.", "52.2"),
            Snap(12, 102, "HYOSUNG HEAVY INDUSTRIES CORPORATION (HICO)", "52.2"),
            Snap(13, 103, "Rongxin Huiko Electric Co Ltd.", "52.2"),
            Snap(14, 104, "NARI Technology Co., Ltd. CEPRI", "52.2"),
            Snap(15, 105, "Hitachi Energy", "52.2", country: "SWEDEN"),
            Snap(16, 106, "Grid Solutions Oy", "52.2", country: "FINLAND"),
        };

        var changes = new ComparisonEngine().Compare(previous, current);

        var newRecords = changes.Where(c => c.ChangeType == ChangeType.NewRecord).ToList();
        Assert.Equal(4, newRecords.Count);
        Assert.Contains(newRecords, c => c.CurrentEntryId == 15); // Hitachi Energy
        Assert.DoesNotContain(changes, c => c.SupplierId == 101 && c.ChangeType != ChangeType.NewRecord);
        Assert.DoesNotContain(changes, c => c.SupplierId == 102); // unchanged -> no candidate at all
    }

    [Fact]
    public void SupplierPresentInBothWithNoFieldChanges_ProducesNoCandidates()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "ABB", "1.1") };
        var current = new List<EntrySnapshot> { Snap(2, 1, "ABB", "1.1") };

        var changes = new ComparisonEngine().Compare(previous, current);
        Assert.Empty(changes);
    }

    [Fact]
    public void StatusChange_QualifiedToSuspended_IsDetected()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "ABB", "1.1", status: ApprovalStatus.Qualified) };
        var current = new List<EntrySnapshot> { Snap(2, 1, "ABB", "1.1", status: ApprovalStatus.Suspended) };

        var changes = new ComparisonEngine().Compare(previous, current);

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.StatusChanged, change.ChangeType);
        Assert.Equal("Qualified", change.PreviousValue);
        Assert.Equal("Suspended", change.CurrentValue);
    }

    [Fact]
    public void ModelRatingChange_WithKvKeyword_ClassifiesAsVoltageChanged()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "DEF Co", "4.1", modelRating: "Approved up to 33kV") };
        var current = new List<EntrySnapshot> { Snap(2, 1, "DEF Co", "4.1", modelRating: "Approved up to 13.8kV") };

        var changes = new ComparisonEngine().Compare(previous, current);
        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.VoltageChanged, change.ChangeType);
    }

    [Fact]
    public void ModelRatingChange_WithoutVoltageKeyword_ClassifiesAsRatingChanged()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "DEF Co", "24.1", modelRating: "100 MVA") };
        var current = new List<EntrySnapshot> { Snap(2, 1, "DEF Co", "24.1", modelRating: "150 MVA") };

        var changes = new ComparisonEngine().Compare(previous, current);
        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.RatingChanged, change.ChangeType);
    }

    [Fact]
    public void LocalRepresentativeChange_IsDetectedAsAgentChanged()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "Siemens", "10.1", localRep: null) };
        var current = new List<EntrySnapshot> { Snap(2, 1, "Siemens", "10.1", localRep: "Alfanar Electrical Systems") };

        var changes = new ComparisonEngine().Compare(previous, current);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.AgentChanged);
    }

    [Fact]
    public void CountryChange_IsDetected()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "ABB", "1.1", country: "GERMANY") };
        var current = new List<EntrySnapshot> { Snap(2, 1, "ABB", "1.1", country: "SWITZERLAND") };

        var changes = new ComparisonEngine().Compare(previous, current);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.CountryChanged);
    }

    [Fact]
    public void RemarkChange_OnlyReportedWhenStatusUnchanged()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "ABB", "1.1", remarks: "Old remark text.") };
        var current = new List<EntrySnapshot> { Snap(2, 1, "ABB", "1.1", remarks: "New remark text, warranty extended.") };

        var changes = new ComparisonEngine().Compare(previous, current);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.RemarkChanged);
    }

    [Fact]
    public void RemarkChange_SuppressedWhenStatusAlsoChanged_ToAvoidDoubleReporting()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "ABB", "1.1", status: ApprovalStatus.Qualified, remarks: "Qualified remark.") };
        var current = new List<EntrySnapshot> { Snap(2, 1, "ABB", "1.1", status: ApprovalStatus.Suspended, remarks: "Suspended remark.") };

        var changes = new ComparisonEngine().Compare(previous, current);
        Assert.DoesNotContain(changes, c => c.ChangeType == ChangeType.RemarkChanged);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.StatusChanged);
    }

    [Fact]
    public void MaterialRemovedEntirely_AllEntriesReportAsRemoved()
    {
        var previous = new List<EntrySnapshot>
        {
            Snap(1, 1, "Supplier X", "39.1"),
        };
        var current = new List<EntrySnapshot>(); // 39.1 was confirmed removed between the two real releases

        var changes = new ComparisonEngine().Compare(previous, current);
        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.RemovedRecord, change.ChangeType);
    }

    [Fact]
    public void MaterialCodeRenumbered_WithoutRemap_IsRemovedPlusNew()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "Supplier X", "10.5") };
        var current = new List<EntrySnapshot> { Snap(2, 1, "Supplier X", "10.6") };

        var changes = new ComparisonEngine().Compare(previous, current); // no remap supplied

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.RemovedRecord && c.MaterialCode == "10.5");
        Assert.Contains(changes, c => c.ChangeType == ChangeType.NewRecord && c.MaterialCode == "10.6");
    }

    [Fact]
    public void MaterialCodeRenumbered_WithConfirmedRemap_ComparesAsSameMaterial()
    {
        var previous = new List<EntrySnapshot> { Snap(1, 1, "Supplier X", "10.5", status: ApprovalStatus.Qualified) };
        var current = new List<EntrySnapshot> { Snap(2, 1, "Supplier X", "10.6", status: ApprovalStatus.Suspended) };
        var remaps = new List<CodeRemap> { new("10.5", "10.6") };

        var changes = new ComparisonEngine().Compare(previous, current, remaps);

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.StatusChanged, change.ChangeType);
    }
}
