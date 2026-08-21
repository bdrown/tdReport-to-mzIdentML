using NRTDP.tdReportConverter;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// An in-memory <see cref="IOpenTDReport"/> holding one protein, one proteoform and one scan.
///
/// MzidmlWriter only ever talks to this interface, so driving it from a stub exercises the real
/// document-assembly logic without needing a multi-GB tdReport. What each test varies is which
/// (result set, raw file) pairs have hits - that is the condition the writer has to react to.
/// </summary>
internal sealed class StubTdReport : IOpenTDReport
{
    public const int RawFileId = 110;
    public const int IsoformId = 1;
    public const int ChemId = 1;
    public const int HitId = 900;
    public const int ScanNumber = 4343;
    private const string ProteinSequence = "MKSLVLLLCLAQLWGCHSAPHGPGLIYR";

    private readonly Dictionary<int, string> _resultSets;
    private readonly HashSet<int> _resultSetsWithHits;

    /// <param name="resultSets">Result set id -> search name, as GetResultSets would return.</param>
    /// <param name="resultSetsWithHits">Which of those actually identified something.</param>
    public StubTdReport(Dictionary<int, string> resultSets, params int[] resultSetsWithHits)
    {
        _resultSets = resultSets;
        _resultSetsWithHits = new HashSet<int>(resultSetsWithHits);
    }

    private static Dictionary<int, string> TwoSearches() =>
        new() { [1] = "BioMarker", [2] = "Tight Absolute Mass" };

    /// <summary>Two searches, as a TDPortal report has, with only "Tight Absolute Mass" finding anything.</summary>
    public static StubTdReport OneEmptySearch() => new(TwoSearches(), 2);

    /// <summary>Both searches identify something.</summary>
    public static StubTdReport BothSearchesPopulated() => new(TwoSearches(), 1, 2);

    /// <summary>Neither search identified anything at this FDR.</summary>
    public static StubTdReport NothingIdentified() => new(TwoSearches());

    public bool IsProSightPD => false;
    public string? SoftwareVersion => "4.0.0.81";

    public Dictionary<int, string> GetResultSets() => _resultSets;

    public Dictionary<int, Tuple<string, string>> GetDataFiles() =>
        new() { [RawFileId] = Tuple.Create("sample_techrep01.raw", @"C:\data\sample_techrep01.raw") };

    public bool HasHits(int ResultSetId, int dataFileId, double FDR = 0.05) =>
        _resultSetsWithHits.Contains(ResultSetId) && dataFileId == RawFileId;

    public Dictionary<int, Dictionary<int, SpectrumIdentificationItem_Hit>> CreateBatchOfHitsWithIons(
        int ResultSetId, int dataFileId, double FDR = 0.05)
    {
        if (!HasHits(ResultSetId, dataFileId, FDR))
            return new Dictionary<int, Dictionary<int, SpectrumIdentificationItem_Hit>>();

        var hit = new SpectrumIdentificationItem_Hit
        {
            ChemId = ChemId,
            BioId = new HashSet<int> { 1 },
            IsoformId = new HashSet<int> { IsoformId },
            ObsPreMass = 13754.12308,
            TheoPreMass = 13753.89530,
            Scans = new HashSet<int> { ScanNumber },
            FragmentIons = new Dictionary<int, Dictionary<string, IList<FragmentIon>>>
            {
                [3] = new Dictionary<string, IList<FragmentIon>>
                {
                    ["B"] = new List<FragmentIon>
                    {
                        new() { HitId = HitId, ObservedMz = 1291.7046, TheoreticalMz = 1291.6924, Charge = 3, IonNumber = 38, IonType = "B" },
                    },
                },
            },
            PScore = 1.0e-30,
            EValue = 1.0e-25,
            CScore = 400.0,
            Cleavages = 12,
            GlobalQValue = 0.0001,
        };

        return new Dictionary<int, Dictionary<int, SpectrumIdentificationItem_Hit>>
        {
            [ScanNumber] = new Dictionary<int, SpectrumIdentificationItem_Hit> { [HitId] = hit },
        };
    }

    public Dictionary<int, Dictionary<int, ProteinAmbiguityGroup>> GetproteinDetectiondata(
        int ResultSetId, int dataFileId, double FDR = 0.05)
    {
        if (!HasHits(ResultSetId, dataFileId, FDR))
            return new Dictionary<int, Dictionary<int, ProteinAmbiguityGroup>>();

        var group = new ProteinAmbiguityGroup
        {
            HitId = new HashSet<int> { HitId },
            ChemId = ChemId,
            BioId = new HashSet<int> { 1 },
            IsoformId = IsoformId,
            IsoformGlobalQvalue = 0.0001,
            EntryGlobalQValue = 0.0001,
        };

        return new Dictionary<int, Dictionary<int, ProteinAmbiguityGroup>>
        {
            [IsoformId] = new Dictionary<int, ProteinAmbiguityGroup> { [ChemId] = group },
        };
    }

    public List<DBSequence> GetDBSequences(double FDR, int? dataSetId = null) =>
        new()
        {
            new DBSequence
            {
                ID = IsoformId,
                Accession = "P02765",
                Sequence = ProteinSequence,
                UniProtID = "P02765",
                TaxonID = 9606,
                SciName = "Homo sapiens",
                Description = "Alpha-2-HS-glycoprotein",
            },
        };

    public IEnumerable<ChemicalProetoform> GetChemicalProteoforms(double FDR, int? dataSetId = null) =>
        new[]
        {
            new ChemicalProetoform
            {
                ID = ChemId,
                DBSequenceID = IsoformId,
                BioId = 1,
                Sequence = ProteinSequence,
                StartIndex = 0,
                EndIndex = ProteinSequence.Length - 1,
                IsoformSeqence = ProteinSequence,
            },
        };

    public IEnumerable<BiologicalProetoform> GetBiologicalProteoforms(double FDR, int? dataSetId = null) =>
        new[]
        {
            new BiologicalProetoform
            {
                ID = 1,
                DBSequenceID = IsoformId,
                ChemId = ChemId,
                Sequence = ProteinSequence,
                StartIndex = 0,
                EndIndex = ProteinSequence.Length - 1,
                IsoformSeqence = ProteinSequence,
                ProteoformQValue = 0.0001,
            },
        };

    public Dictionary<string, double> GetMassTable() => new() { ["A"] = 71.03711, ["C"] = 103.00919 };

    // TDPortal search parameters; an empty dictionary is also valid (ProSight PD) and the writer guards for it.
    public Dictionary<string, string> GetResultSetParameters(int ResultSetId) =>
        new() { ["fragment_tolerance"] = "10 ppm", ["precursor_window_tolerance"] = "10 ppm" };

    public Dictionary<string, Dictionary<string, string>> GetParameters() =>
        new() { ["Generate Report"] = new Dictionary<string, string> { ["FdrCutoff"] = "0.01" } };

    public BioMod ModLookup(int? modId, string? ModificationSetId, int startIndex, int chemId) =>
        throw new NotSupportedException("The stub proteoform carries no modifications.");

    public List<BioMod> ParseModHash(string ModHash, int chemId) => new();

    public Dictionary<int, Dictionary<string, IList<FragmentIon>>> GetFragmentsforHit(int hitId) => new();

    public void Dispose() { }
}
