using NRTDP.tdReportConverter;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// The metadata file supplies mzIdentML fields a tdReport does not contain. Every section and
/// field is optional, so the contract worth pinning down is "absent means fall back", never throw.
/// </summary>
public class MzidMetadataTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string WriteJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mzid-metadata-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void Null_path_yields_all_defaults()
    {
        var metadata = MzidMetadata.Load(null);

        Assert.Null(metadata.Submitter);
        Assert.Null(metadata.Software);
        Assert.Null(metadata.SearchDatabase);
        Assert.Null(metadata.SpectraData);
    }

    [Fact]
    public void Missing_file_yields_all_defaults_rather_than_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.json");

        var metadata = MzidMetadata.Load(missing);

        Assert.Null(metadata.Submitter);
        Assert.Null(metadata.Software);
    }

    [Fact]
    public void Absent_sections_stay_null_while_present_ones_load()
    {
        var path = WriteJson("""
            { "software": { "name": "ProSight PD", "version": "4.2" } }
            """);

        var metadata = MzidMetadata.Load(path);

        Assert.Equal("ProSight PD", metadata.Software?.Name);
        Assert.Equal("4.2", metadata.Software?.Version);
        Assert.Null(metadata.Software?.Uri);        // absent field within a present section
        Assert.Null(metadata.Submitter);            // absent section
        Assert.Null(metadata.SearchDatabase);
    }

    [Fact]
    public void All_sections_load()
    {
        var path = WriteJson("""
            {
              "submitter":      { "firstName": "Ada", "lastName": "Lovelace",
                                  "email": "ada@example.org", "organization": "Example University" },
              "software":       { "name": "TDPortal", "version": "4.0.0.81", "uri": "https://example.org/" },
              "searchDatabase": { "name": "Example DB", "version": "2026_01", "location": "file:///db.fasta",
                                  "taxonomy": "Homo sapiens", "numDatabaseSequences": 12345 },
              "spectraData":    { "fileFormatAccession": "MS:1000563", "fileFormatName": "Thermo RAW format",
                                  "idFormatAccession": "MS:1000768", "idFormatName": "Thermo nativeID format" }
            }
            """);

        var metadata = MzidMetadata.Load(path);

        Assert.Equal("Ada", metadata.Submitter?.FirstName);
        Assert.Equal("Example University", metadata.Submitter?.Organization);
        Assert.Equal("4.0.0.81", metadata.Software?.Version);
        Assert.Equal(12345, metadata.SearchDatabase?.NumDatabaseSequences);
        Assert.Equal("Thermo RAW format", metadata.SpectraData?.FileFormatName);
    }

    [Fact]
    public void Property_names_are_case_insensitive()
    {
        var path = WriteJson("""
            { "Software": { "Name": "TDPortal", "VERSION": "4.0.0.81" } }
            """);

        var metadata = MzidMetadata.Load(path);

        Assert.Equal("TDPortal", metadata.Software?.Name);
        Assert.Equal("4.0.0.81", metadata.Software?.Version);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        var path = WriteJson("""
            {
              // hand-edited files pick these up; the loader allows them
              "software": { "name": "TDPortal", },
            }
            """);

        var metadata = MzidMetadata.Load(path);

        Assert.Equal("TDPortal", metadata.Software?.Name);
    }

    [Fact]
    public void Empty_json_object_yields_all_defaults()
    {
        var path = WriteJson("{}");

        var metadata = MzidMetadata.Load(path);

        Assert.Null(metadata.Submitter);
        Assert.Null(metadata.Software);
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles.Where(File.Exists))
            File.Delete(path);
    }
}
