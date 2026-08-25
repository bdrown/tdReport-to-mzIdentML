using System.Xml.Linq;
using NRTDP.tdReportConverter;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// The protein and the spectrum sides of a report are read by two queries that do not filter
/// identically: GetproteinDetectiondata has neither the MS2-scan restriction nor the inner joins on
/// HitScore that the hits query applies. So it can name a hit that never received a
/// SpectrumIdentificationItem, and the writer has to cope without producing an element the schema
/// rejects as empty - PeptideHypothesis needs a SpectrumIdentificationItemRef,
/// ProteinDetectionHypothesis needs a PeptideHypothesis, ProteinAmbiguityGroup needs one of those.
/// </summary>
public class ProteinDetectionTests
{
    private static readonly XNamespace Mzid = "http://psidev.info/psi/pi/mzIdentML/1.1";
    private const double FDR = 0.01;

    private static MemoryStream WriteDocument(StubTdReport report)
    {
        var stream = new MemoryStream();
        MzidmlWriter.WriteDocument(
            report, stream, new FileInfo(@"C:\reports\example.tdReport"), FDR, StubTdReport.RawFileId);
        stream.Position = 0;
        return stream;
    }

    private static XDocument Parse(MemoryStream document)
    {
        document.Position = 0;
        return XDocument.Load(document);
    }

    [Fact]
    public void Protein_data_for_an_unmatched_hit_still_produces_a_schema_valid_document()
    {
        // Arrange
        using var report = StubTdReport.ProteinDataForUnmatchedHit();

        // Act
        using var document = WriteDocument(report);

        // Assert
        Assert.Empty(MzidSchemaValidator.Validate(document));
    }

    [Fact]
    public void Unmatched_hit_produces_no_ProteinAmbiguityGroup()
    {
        // Its only hit has no SpectrumIdentificationItem, so every element that would enclose the
        // reference has to go too - not just the PeptideHypothesis.
        using var report = StubTdReport.ProteinDataForUnmatchedHit();

        using var document = WriteDocument(report);

        Assert.Empty(Parse(document).Descendants(Mzid + "ProteinAmbiguityGroup"));
    }

    [Fact]
    public void Every_PeptideHypothesis_written_holds_at_least_one_item_reference()
    {
        using var report = StubTdReport.ProteinDataForUnmatchedHit();

        using var document = WriteDocument(report);

        Assert.All(Parse(document).Descendants(Mzid + "PeptideHypothesis"),
                   hypothesis => Assert.NotEmpty(hypothesis.Elements(Mzid + "SpectrumIdentificationItemRef")));
    }

    [Fact]
    public void Matched_hits_still_produce_their_protein_groups()
    {
        // Guards against over-filtering: the fix must not drop protein groups that do have matches.
        using var report = StubTdReport.BothSearchesPopulated();

        using var document = WriteDocument(report);

        var parsed = Parse(document);
        Assert.NotEmpty(parsed.Descendants(Mzid + "ProteinAmbiguityGroup"));
        Assert.All(parsed.Descendants(Mzid + "PeptideHypothesis"),
                   hypothesis => Assert.NotEmpty(hypothesis.Elements(Mzid + "SpectrumIdentificationItemRef")));
        Assert.Empty(MzidSchemaValidator.Validate(document));
    }

    [Fact]
    public void Every_item_reference_points_at_an_item_that_was_written()
    {
        using var report = StubTdReport.BothSearchesPopulated();

        using var document = WriteDocument(report);

        var parsed = Parse(document);
        var written = parsed.Descendants(Mzid + "SpectrumIdentificationItem")
                            .Select(e => (string?)e.Attribute("id"))
                            .ToHashSet();
        var referenced = parsed.Descendants(Mzid + "SpectrumIdentificationItemRef")
                               .Select(e => (string?)e.Attribute("spectrumIdentificationItem_ref"))
                               .ToList();

        Assert.NotEmpty(referenced);
        Assert.All(referenced, r => Assert.Contains(r, written));
    }
}
