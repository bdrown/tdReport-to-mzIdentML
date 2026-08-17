using System;
using System.IO;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NRTDP.tdReportConverter
{
    /// <summary>
    /// Optional overrides for mzIdentML metadata that the tdReport does not contain
    /// (submitter contact, search database identity, software details, spectra file
    /// format). Loaded from a YAML or JSON file; any section/field left absent falls back
    /// to a built-in default applied by <see cref="MzidmlWriter"/>.
    /// See mzid-metadata.example.yaml / mzid-metadata.example.json.
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

        // camelCase keys to match the JSON form, and unmatched keys are ignored so a typo
        // degrades to the built-in default rather than throwing mid-conversion.
        private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithCaseInsensitivePropertyMatching()
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>
        /// Loads metadata overrides from a YAML (.yaml/.yml) or JSON (.json) file, chosen by
        /// extension; anything else is tried as YAML and then as JSON. Returns an empty instance
        /// (all defaults) when <paramref name="path"/> is null/empty or the file is missing.
        /// </summary>
        public static MzidMetadata Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new MzidMetadata();

            var text = File.ReadAllText(path);
            var extension = Path.GetExtension(path);

            if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
                return FromYaml(text);

            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                return FromJson(text);

            // Unknown extension: YAML 1.2 is a superset of JSON, so it parses both. Fall back to
            // the JSON reader anyway, since it tolerates // comments and trailing commas.
            try
            {
                return FromYaml(text);
            }
            catch (YamlDotNet.Core.YamlException)
            {
                return FromJson(text);
            }
        }

        private static MzidMetadata FromJson(string text) =>
            JsonSerializer.Deserialize<MzidMetadata>(text, _jsonOptions) ?? new MzidMetadata();

        private static MzidMetadata FromYaml(string text) =>
            _yamlDeserializer.Deserialize<MzidMetadata>(text) ?? new MzidMetadata();
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
