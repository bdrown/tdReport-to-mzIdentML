using System;
using System.Collections.Generic;

namespace NRTDP.tdReportConverter
{
    /// <summary>
    /// 
    /// </summary>
    public interface IOpenTDReport : IDisposable
    {
        /// <summary>
        /// True when the report came from ProSight PD rather than TDPortal (detected via the
        /// empty ResultParameter table). Drives AnalysisSoftware provenance and parameter guards.
        /// </summary>
        bool IsProSightPD { get; }

        /// <summary>
        /// Software version recorded in the report, or null when none is stored (e.g. ProSight PD).
        /// For TDPortal this is the analysis codeset from DbMetadata. The JSON metadata override
        /// takes precedence over this when present.
        /// </summary>
        string? SoftwareVersion { get; }

        List<DBSequence> GetDBSequences(double FDR, int? dataSetId = null);
        Dictionary<string, double> GetMassTable();
        Dictionary<int, string> GetResultSets();

        Dictionary<string, string> GetResultSetParameters(int ResultSetId);
        Dictionary<string, Dictionary<string, string>> GetParameters();
        Dictionary<int, Tuple<string, string>> GetDataFiles();
        IEnumerable<BiologicalProetoform> GetBiologicalProteoforms(double FDR, int? dataSetId = null);
        IEnumerable<ChemicalProetoform> GetChemicalProteoforms(double FDR, int? dataSetId = null);
        BioMod ModLookup(int? modId, string? ModificationSetId, int startIndex, int chemId);
        public List<BioMod> ParseModHash(string ModHash, int chemId);

        Dictionary<int, Dictionary<string, IList<FragmentIon>>> GetFragmentsforHit(int hitId);


        Dictionary<int, Dictionary<int, SpectrumIdentificationItem_Hit>> CreateBatchOfHitsWithIons(int ResultSetId, int dataFileId, double FDR = 0.05);

            Dictionary<int, Dictionary<int, ProteinAmbiguityGroup>> GetproteinDetectiondata(int ResultSetId, int dataFileId, double FDR = 0.05);
    }
}
