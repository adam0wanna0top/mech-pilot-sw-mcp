using MechPilot.SwMcp.Tools.Internal;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 for <see cref="PartKind"/> — the SW-free classification of a part as ours
/// vs imported from its non-boot feature type names (M40). The live component
/// walk in InspectAssemblyTool is covered by the M40 L2 integration test; the
/// "imported" signal (MBimport) was confirmed by the M40 probe.
/// </summary>
public class PartKindTests
{
    [Theory]
    [InlineData("MBimport", true)]        // confirmed STEP/neutral import node (M40 probe)
    [InlineData("ImportedFeature", true)] // defensive aliases
    [InlineData("Imported", true)]
    [InlineData("Extrusion", false)]
    [InlineData("ProfileFeature", false)]
    [InlineData("Revolution", false)]
    public void IsImportFeatureType_detects_import_nodes(string typeName, bool expected) =>
        Assert.Equal(expected, PartKind.IsImportFeatureType(typeName));

    [Fact]
    public void ClassifyPart_import_node_means_imported() =>
        Assert.Equal(PartKind.Imported, PartKind.ClassifyPart(new[] { "MBimport" }));

    [Fact]
    public void ClassifyPart_import_wins_over_build_features() =>
        // Defensive: if both somehow appear, an import node still means a dumb body.
        Assert.Equal(PartKind.Imported, PartKind.ClassifyPart(new[] { "Extrusion", "MBimport" }));

    [Fact]
    public void ClassifyPart_build_features_mean_ourPart() =>
        Assert.Equal(PartKind.OurPart, PartKind.ClassifyPart(new[] { "ProfileFeature", "Extrusion" }));

    [Fact]
    public void ClassifyPart_no_user_features_is_unknown() =>
        Assert.Equal(PartKind.Unknown, PartKind.ClassifyPart(Array.Empty<string>()));
}
