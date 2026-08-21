using System.Xml;
using System.Xml.Schema;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// Validates a generated document against the published mzIdentML 1.1.0 schema.
///
/// Identity constraints are on deliberately: mzIdentML 1.1 declares no xsd:ID/IDREF and instead
/// expresses every *_ref as an xsd:keyref, so leaving them off would let dangling references
/// through - exactly the failure mode that omitting a SpectrumIdentificationList could introduce.
/// </summary>
internal static class MzidSchemaValidator
{
    private const string MzidNamespace = "http://psidev.info/psi/pi/mzIdentML/1.1";

    /// <summary>Every schema violation in <paramref name="document"/>; empty means valid.</summary>
    public static IReadOnlyList<string> Validate(Stream document)
    {
        var problems = new List<string>();

        var schemas = new XmlSchemaSet();
        schemas.Add(MzidNamespace, Path.Combine(AppContext.BaseDirectory, "schema", "mzIdentML1.1.0.xsd"));

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings
                              | XmlSchemaValidationFlags.ProcessIdentityConstraints,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) =>
            problems.Add($"[{e.Severity}] line {e.Exception?.LineNumber}: {e.Message}");

        document.Position = 0;
        using var reader = XmlReader.Create(document, settings);
        while (reader.Read()) { }

        return problems;
    }
}
