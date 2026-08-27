using System.Xml.Linq;
using NRTDP.tdReportConverter;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// A search that identifies nothing for a given raw file must be left out of the document entirely.
///
/// mzIdentML requires every SpectrumIdentificationList to hold at least one
/// SpectrumIdentificationResult, but the list - and the SpectrumIdentification and
/// InputSpectrumIdentifications that reference it - are written before the results are known. Real
/// conversions hit this: one raw file in a 120-file run produced no BioMarker hits at 1% FDR and the
/// resulting .mzid failed validation with "SpectrumIdentificationList has incomplete content".
/// </summary>
public class EmptyResultSetTests
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
    public void Document_with_an_empty_search_is_schema_valid()
    {
        // Arrange
        using var report = StubTdReport.OneEmptySearch();

        // Act
        using var document = WriteDocument(report);

        // Assert
        Assert.Empty(MzidSchemaValidator.Validate(document));
    }

    [Fact]
    public void Empty_search_gets_no_SpectrumIdentificationList()
    {
        using var report = StubTdReport.OneEmptySearch();

        using var document = WriteDocument(report);

        var listIds = Parse(document).Descendants(Mzid + "SpectrumIdentificationList")
                                     .Select(e => (string?)e.Attribute("id"))
                                     .ToList();
        Assert.Equal(new[] { "SIL_2" }, listIds);
    }

    [Fact]
    public void Every_SpectrumIdentificationList_written_holds_at_least_one_result()
    {
        using var report = StubTdReport.OneEmptySearch();

        using var document = WriteDocument(report);

        var lists = Parse(document).Descendants(Mzid + "SpectrumIdentificationList").ToList();
        Assert.NotEmpty(lists);
        Assert.All(lists, list => Assert.NotEmpty(list.Elements(Mzid + "SpectrumIdentificationResult")));
    }

    [Fact]
    public void No_reference_points_at_a_SpectrumIdentificationList_that_was_not_written()
    {
        using var report = StubTdReport.OneEmptySearch();

        using var document = WriteDocument(report);

        var parsed = Parse(document);
        var written = parsed.Descendants(Mzid + "SpectrumIdentificationList")
                            .Select(e => (string?)e.Attribute("id"))
                            .ToHashSet();
        var referenced = parsed.Descendants()
                               .Select(e => (string?)e.Attribute("spectrumIdentificationList_ref"))
                               .Where(r => r is not null)
                               .ToList();

        Assert.NotEmpty(referenced);   // SpectrumIdentification + InputSpectrumIdentifications
        Assert.All(referenced, r => Assert.Contains(r, written));
    }

    [Fact]
    public void Populated_searches_are_all_kept()
    {
        // Guards against over-filtering: the fix must not drop searches that did identify something.
        using var report = StubTdReport.BothSearchesPopulated();

        using var document = WriteDocument(report);

        var listIds = Parse(document).Descendants(Mzid + "SpectrumIdentificationList")
                                     .Select(e => (string?)e.Attribute("id"))
                                     .OrderBy(id => id)
                                     .ToList();
        Assert.Equal(new[] { "SIL_1", "SIL_2" }, listIds);
        Assert.Empty(MzidSchemaValidator.Validate(document));
    }

    [Fact]
    public void Search_protocols_are_kept_for_searches_that_found_nothing()
    {
        // The protocol records a search that ran, which stays true when it identified nothing, and
        // the schema permits a protocol no SpectrumIdentification references.
        using var report = StubTdReport.OneEmptySearch();

        using var document = WriteDocument(report);

        var protocolIds = Parse(document).Descendants(Mzid + "SpectrumIdentificationProtocol")
                                         .Select(e => (string?)e.Attribute("id"))
                                         .OrderBy(id => id)
                                         .ToList();
        Assert.Equal(new[] { "SIP_1", "SIP_2" }, protocolIds);
    }

    [Fact]
    public void Raw_file_that_identified_nothing_has_no_populated_result_sets()
    {
        // What the per-raw-file entry points check before deciding to skip the file: with nothing to
        // write there is no valid document to produce, since the schema has no "searched, found
        // nothing" form.
        using var report = StubTdReport.NothingIdentified();

        var populated = MzidmlWriter.PopulatedResultSets(report, FDR, StubTdReport.RawFileId);

        Assert.Empty(populated);
    }

    [Fact]
    public void Populated_result_sets_are_those_the_reader_reports_hits_for()
    {
        using var report = StubTdReport.OneEmptySearch();

        var populated = MzidmlWriter.PopulatedResultSets(report, FDR, StubTdReport.RawFileId);

        Assert.Equal(new[] { 2 }, populated.Keys.ToArray());
    }
}
