using System.IO;
using System.Text.Json;

namespace NRTDP.tdReportConverter
{
    /// <summary>
    /// Optional overrides for mzIdentML metadata that the tdReport does not contain
    /// (submitter contact, search database identity, software details, spectra file
    /// format). Loaded from a JSON file; any section/field left absent falls back to a
    /// built-in default applied by <see cref="MzidmlWriter"/>. See mzid-metadata.example.json.
    /// </summary>
    public sealed class MzidMetadata
    {
        public SubmitterInfo? Submitter { get; set; }
        public SoftwareInfo? Software { get; set; }
        public SearchDatabaseInfo? SearchDatabase { get; set; }
        public SpectraDataInfo? SpectraData { get; set; }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Loads metadata overrides from a JSON file. Returns an empty instance (all
        /// defaults) when <paramref name="path"/> is null/empty or the file is missing.
        /// </summary>
        public static MzidMetadata Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new MzidMetadata();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MzidMetadata>(json, _jsonOptions) ?? new MzidMetadata();
        }
    }

    public sealed class SubmitterInfo
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Organization { get; set; }
        public string? OrganizationUri { get; set; }
    }

    public sealed class SoftwareInfo
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Uri { get; set; }
    }

    public sealed class SearchDatabaseInfo
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Location { get; set; }
        public string? Taxonomy { get; set; }
        public long? NumDatabaseSequences { get; set; }
    }

    public sealed class SpectraDataInfo
    {
        public string? FileFormatAccession { get; set; }
        public string? FileFormatName { get; set; }
        public string? IdFormatAccession { get; set; }
        public string? IdFormatName { get; set; }
    }
}
