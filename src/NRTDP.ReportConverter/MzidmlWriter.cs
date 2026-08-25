using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace NRTDP.tdReportConverter
{
    /// <summary>
    /// Writes mzidentml (aka mzidml) files
    /// </summary>
    public sealed class MzidmlWriter : IDisposable
    {
        private XmlWriter _writer;
        private readonly MzidMetadata _metadata;

        // Built-in defaults for metadata the tdReport doesn't contain; each is overridden
        // per field by the JSON config (see MzidMetadata / mzid-metadata.example.json).
        private const string DefaultFirstName = "Neil";
        private const string DefaultLastName = "Kelleher";
        private const string DefaultOrganization = "Northwestern University";
        private const string DefaultSoftwareUri = "http://www.kelleher.northwestern.edu/";
        private const string DefaultDbName = "Unknown database";
        private const string DefaultDbLocation = "unknown";
        private const string DefaultRawFormatAccession = "MS:1000563";
        private const string DefaultRawFormatName = "Thermo RAW format";
        private const string DefaultIdFormatAccession = "MS:1000768";
        private const string DefaultIdFormatName = "Thermo nativeID format";

        private MzidmlWriter(Stream stream, Encoding encoding, MzidMetadata? metadata)
        {
            _writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = encoding, Indent = true });
            _metadata = metadata ?? new MzidMetadata();
        }

        private static string AnalysisSoftwareId(IOpenTDReport db) => db.IsProSightPD ? "AS_ProSightPD" : "AS_TDPortal";

        /// <summary>
        /// Writes one complete mzIdentML document from an already-open reader. The section order is
        /// fixed by the schema, so all three entry points share it from here; it is internal so tests
        /// can drive the writer from a stub <see cref="IOpenTDReport"/> rather than a real tdReport.
        /// </summary>
        /// <param name="inputFileInfo">Names the source report; only Name and FullName are read, so
        /// the path does not have to exist.</param>
        /// <param name="populatedResultSets">Precomputed result of <see cref="PopulatedResultSets"/>
        /// when the caller has already established it; computed on demand when null.</param>
        internal static void WriteDocument(IOpenTDReport db, Stream output, FileInfo inputFileInfo, double FDR, int? dataSetId, MzidMetadata? metadata = null, Dictionary<int, string>? populatedResultSets = null)
        {
            // Leaves `output` open: XmlWriterSettings.CloseOutput defaults to false and the caller
            // owns the stream.
            using MzidmlWriter writer = new(output, Encoding.ASCII, metadata);
            writer._populatedResultSets = populatedResultSets;
            writer.WriteStartDoc();
            writer.WriteMzIDStartElement(inputFileInfo.Name);
            writer.WriteMzIDCVList();
            writer.WriteAnalysisSoftwareList(db);
            writer.WriteProviderAndAuditCollection();
            writer.WriteSequenceCollection(db, FDR, dataSetId);
            writer.WriteAnalysisCollection(db, FDR, dataSetId);
            writer.WriteDataCollection(db, inputFileInfo, FDR, dataSetId);
        }

        /// <summary>
        /// The result sets that will actually yield hits for <paramref name="dataSetId"/>, or across
        /// every raw file when it is null.
        ///
        /// A SpectrumIdentificationList must hold at least one SpectrumIdentificationResult, but it
        /// has to be declared - together with the SpectrumIdentification and
        /// InputSpectrumIdentifications that reference it, both written earlier in the document -
        /// before its results are streamed in. So result sets that will come back empty have to be
        /// recognised up front and omitted from all three places; dropping only the list would leave
        /// those two references dangling.
        /// </summary>
        internal static Dictionary<int, string> PopulatedResultSets(IOpenTDReport db, double FDR, int? dataSetId)
        {
            var rawFileIds = dataSetId.HasValue
                ? new List<int> { dataSetId.Value }
                : db.GetDataFiles().Keys.ToList();

            return db.GetResultSets()
                     .Where(resultSet => rawFileIds.Any(id => db.HasHits(resultSet.Key, id, FDR)))
                     .ToDictionary(resultSet => resultSet.Key, resultSet => resultSet.Value);
        }

        // One writer instance writes exactly one document, for one dataSetId, so this cannot change
        // between the AnalysisCollection and AnalysisData sections.
        private Dictionary<int, string>? _populatedResultSets;

        private Dictionary<int, string> GetPopulatedResultSets(IOpenTDReport db, double FDR, int? dataSetId) =>
            _populatedResultSets ??= PopulatedResultSets(db, FDR, dataSetId);

        /// <summary>
        /// Writes to a sibling .partial file and renames it into place once the document is complete.
        ///
        /// This is what lets skipExisting trust the output folder: a run killed mid-write - by a
        /// crash, a Ctrl-C, or a reboot for an update - leaves a truncated .partial rather than a
        /// truncated .mzid, so a file at <paramref name="outputPath"/> is always a whole document and
        /// is always safe to skip. Without it, resuming would silently keep exactly the one broken
        /// file the interruption produced.
        /// </summary>
        internal static void WriteAtomically(string outputPath, Action<Stream> writeContent)
        {
            var partialPath = outputPath + ".partial";
            try
            {
                using (FileStream stream = File.Create(partialPath))
                {
                    writeContent(stream);
                }

                File.Move(partialPath, outputPath, overwrite: true);
            }
            catch
            {
                // Best effort: a leftover .partial is inert, and losing the original failure to a
                // cleanup error would be worse.
                try { File.Delete(partialPath); } catch { }
                throw;
            }
        }

        /// <summary>A scratch file that is removed when the conversion ends, however it ends.</summary>
        private sealed class ScratchFile : IDisposable
        {
            public string Path { get; } = System.IO.Path.GetTempFileName();

            public void Dispose() => File.Delete(Path);
        }

        /// <summary>Whether this raw file's output already exists and should be left alone.</summary>
        private static bool AlreadyConverted(bool skipExisting, string outputPath, string rawFileName)
        {
            if (!skipExisting || !File.Exists(outputPath))
                return false;

            Console.WriteLine($"Skipping {rawFileName}: {Path.GetFileName(outputPath)} already exists.");
            return true;
        }

        /// <summary>
        /// The result sets to write for this raw file, or null when it identified nothing.
        ///
        /// mzIdentML cannot express "this file was searched and nothing passed": both
        /// SpectrumIdentification and SpectrumIdentificationList are minOccurs=1, so the only
        /// alternatives are a valid document with results or no document at all. The answer is
        /// handed to WriteDocument rather than recomputed there, since establishing it costs one
        /// existence query per result set.
        /// </summary>
        private static Dictionary<int, string>? ResultSetsToWrite(IOpenTDReport db, double FDR, int dataSetId, string rawFileName)
        {
            var populated = PopulatedResultSets(db, FDR, dataSetId);
            if (populated.Count > 0)
                return populated;

            Console.WriteLine($"Skipping {rawFileName}: nothing identified at {FDR} FDR.");
            return null;
        }

        /// <summary>
        /// Refuses a report in which two raw files would be written to the same output name.
        ///
        /// Output names come from the raw file's base name, so two raw files differing only by
        /// directory map to one path: without skipExisting the second silently replaces the first,
        /// with it the second is skipped outright. Either way a set of identifications goes missing
        /// while the run reports success, so this is refused up front instead.
        /// </summary>
        private static void GuardAgainstDuplicateOutputNames(Dictionary<int, Tuple<string, string>> datasets, string extension)
        {
            var collisions = datasets.Values
                .GroupBy(d => Path.GetFileNameWithoutExtension(d.Item1) + extension, StringComparer.OrdinalIgnoreCase)
                .Where(byName => byName.Count() > 1)
                .ToList();

            if (collisions.Count == 0)
                return;

            var detail = string.Join("; ", collisions.Select(
                byName => $"{byName.Key} <- {string.Join(", ", byName.Select(d => d.Item2))}"));

            throw new InvalidOperationException(
                $"Two or more raw files would be written to the same output name, which would lose "
                + $"identifications: {detail}");
        }

        /// <summary>
        /// Converts a tdReport into compressed mzidml files. One for each raw file in the tdReport.
        /// </summary>
        /// <param name="TDReport">The file path for the tdReport</param>
        /// <param name="outputFolder">The output folder for the compressed mzidml files</param>
        /// <param name="FDR">The False Discovery Rate (FDR) used to filter the results</param>
        /// <param name="skipExisting">Leave raw files whose .mzid.gz is already present alone, so an
        /// interrupted run can be resumed without redoing completed files.</param>
        public static void ConvertToSeperateCompressedMzId(string TDReport, string outputFolder, double FDR = 0.05, MzidMetadata? metadata = null, ReportSource source = ReportSource.Auto, bool skipExisting = false)
        {
            // Reused and truncated by every iteration, and removed however the conversion ends -
            // including when every dataset is skipped, which no longer reaches a delete in the loop.
            using var tempFile = new ScratchFile();

            var inputFileInfo = new FileInfo(TDReport);

            using var _db = TDReportVersionCheck(inputFileInfo.FullName, source);

            var datasets = _db.GetDataFiles();
            GuardAgainstDuplicateOutputNames(datasets, ".mzid.gz");

            double count = 0.0;
            foreach (var dataset in datasets)
            {
                var rawFileName = dataset.Value.Item1;
                var outputPath = Path.Join(outputFolder, $"{Path.GetFileNameWithoutExtension(rawFileName)}.mzid.gz");

                // Checked before ResultSetsToWrite so a resumed run does no database work for the
                // files it is going to skip.
                if (AlreadyConverted(skipExisting, outputPath, rawFileName))
                {
                    count++;
                    Console.WriteLine(count / datasets.Count());
                    continue;
                }

                var populatedResultSets = ResultSetsToWrite(_db, FDR, dataset.Key, rawFileName);
                if (populatedResultSets is null)
                {
                    count++;
                    Console.WriteLine(count / datasets.Count());
                    continue;
                }

                // Write the document uncompressed first; gzip cannot be written before the content
                // it wraps is known.
                using (FileStream stream = File.Create(tempFile.Path))
                {
                    WriteDocument(_db, stream, inputFileInfo, FDR, dataset.Key, metadata, populatedResultSets);
                }

                WriteAtomically(outputPath, output =>
                {
                    using FileStream sourcefs = File.OpenRead(tempFile.Path);
                    using var compressionStream = new GZipStream(output, CompressionMode.Compress);
                    sourcefs.CopyTo(compressionStream);
                });

                count++;
                Console.WriteLine(count / datasets.Count());
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="TDReport">The file path for the tdReport</param>
        /// <param name="outputFolder">The output file for the mzidml file (include the .mzidml)</param>
        /// <param name="FDR">The False Discovery Rate (FDR) used to filter the results</param>
        public static void ConvertToSingleMzId(string TDReport, string outputPath, double FDR = 0.05, MzidMetadata? metadata = null, ReportSource source = ReportSource.Auto)
        {
            var inputFileInfo = new FileInfo(TDReport);
            using var _db = TDReportVersionCheck(inputFileInfo.FullName, source);

            // Unlike the per-raw-file entry points there is nothing to skip to, and an empty document
            // would not be schema-valid, so fail rather than write one.
            if (PopulatedResultSets(_db, FDR, null).Count == 0)
                throw new InvalidOperationException(
                    $"{inputFileInfo.Name} identified nothing at {FDR} FDR. mzIdentML requires at least " +
                    "one SpectrumIdentificationList, so there is no valid document to write.");

            using (FileStream stream = File.Create(outputPath))
            {
                WriteDocument(_db, stream, inputFileInfo, FDR, dataSetId: null, metadata);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="TDReport">The file path for the tdReport</param>
        /// <param name="outputFolder">The output folder for the compressed mzidml files</param>
        /// <param name="FDR">The False Discovery Rate (FDR) used to filter the results</param>
        /// <param name="skipExisting">Leave raw files whose .mzid is already present alone, so an
        /// interrupted run can be resumed without redoing completed files.</param>
        public static void ConvertToSeperateMzId(string TDReport, string outputFolder, double FDR = 0.05, IProgress<double>? progress = null, MzidMetadata? metadata = null, ReportSource source = ReportSource.Auto, bool skipExisting = false)
        {
            var inputFileInfo = new FileInfo(TDReport);

            using var _db = TDReportVersionCheck(inputFileInfo.FullName, source);

            var datasets = _db.GetDataFiles();
            GuardAgainstDuplicateOutputNames(datasets, ".mzid");

            int count = 0;
            foreach (var dataset in datasets)
            {
                var rawFileName = dataset.Value.Item1;
                var outputPath = Path.Join(outputFolder, $"{Path.GetFileNameWithoutExtension(rawFileName)}.mzid");

                // Checked before ResultSetsToWrite so a resumed run does no database work for the
                // files it is going to skip.
                if (AlreadyConverted(skipExisting, outputPath, rawFileName))
                {
                    progress?.Report((double)++count / datasets.Count);
                    continue;
                }

                var populatedResultSets = ResultSetsToWrite(_db, FDR, dataset.Key, rawFileName);
                if (populatedResultSets is null)
                {
                    progress?.Report((double)++count / datasets.Count);
                    continue;
                }

                WriteAtomically(outputPath, stream =>
                    WriteDocument(_db, stream, inputFileInfo, FDR, dataset.Key, metadata, populatedResultSets));
                progress?.Report((double)++count / datasets.Count);
            }
        }

        // Spectra source-file format CV terms; default to Thermo, overridable via JSON metadata.
        private void WriteSpectraFileFormat()
        {
            var spectra = _metadata.SpectraData;
            this.WriteStartElement("FileFormat");
            this.WriteCVParam(spectra?.FileFormatAccession ?? DefaultRawFormatAccession,
                              spectra?.FileFormatName ?? DefaultRawFormatName);
            this.WriteEndElement();

            this.WriteStartElement("SpectrumIDFormat");
            this.WriteCVParam(spectra?.IdFormatAccession ?? DefaultIdFormatAccession,
                              spectra?.IdFormatName ?? DefaultIdFormatName);
            this.WriteEndElement();
        }

        private void WriteDataCollection(IOpenTDReport db, FileInfo inputFileInfo, double FDR, int? dataSetId = null)
        {
            this.WriteStartElement("DataCollection");
            this.WriteStartElement("Inputs");
            this.WriteStartElement("SourceFile");
            this.WriteAttributeString("location", $@"{inputFileInfo.FullName}");
            this.WriteAttributeString("id", "SF_1"); // TODO: optional Add FileFormat - needs CVparam for TDReport File
            this.WriteEndElement();

            var searchDb = _metadata.SearchDatabase;
            this.WriteStartElement("SearchDatabase");
            this.WriteAttributeString("location", searchDb?.Location ?? DefaultDbLocation);
            this.WriteAttributeString("id", "db1");
            if (!string.IsNullOrEmpty(searchDb?.Version))
                this.WriteAttributeString("version", searchDb.Version);
            if (searchDb?.NumDatabaseSequences is long numDbSeq)
                this.WriteAttributeString("numDatabaseSequences", $"{numDbSeq}");
            this.WriteStartElement("DatabaseName");
            this.WriteUserParam(searchDb?.Name ?? DefaultDbName);
            this.WriteEndElement(); //end DatabaseName
            // SearchDatabase allows only cvParam after DatabaseName, so taxonomy has to be a
            // CV term rather than a userParam - a userParam here fails schema validation.
            if (!string.IsNullOrEmpty(searchDb?.Taxonomy))
                this.WriteCVParam("MS:1001469", "taxonomy: scientific name", searchDb.Taxonomy);
            this.WriteEndElement(); //end SearchDatabase

            //SpectraDAta
            var dataFiles = db.GetDataFiles();
            if (dataSetId.HasValue)
            {
                this.WriteStartElement("SpectraData");
                this.WriteAttributeString("location", dataFiles[dataSetId.Value].Item2);
                this.WriteAttributeString("id", $"SD_{dataSetId.Value}");
                this.WriteAttributeString("name", $"SD_{dataFiles[dataSetId.Value].Item1}");
                this.WriteSpectraFileFormat();
                this.WriteEndElement();
            }
            else
            {
                foreach (var file in dataFiles)
                {
                    this.WriteStartElement("SpectraData");
                    this.WriteAttributeString("location", file.Value.Item2);
                    this.WriteAttributeString("id", $"SD_{file.Key}");
                    this.WriteAttributeString("name", $"SD_{file.Value.Item1}");
                    this.WriteSpectraFileFormat();
                    this.WriteEndElement();
                }
            }

            this.WriteEndElement(); //End inputs

            //AnalysisData goes here

            this.WriteAnalysisData(db, FDR, dataSetId);

            this.WriteEndElement(); //End DataCollection
        }
        private void WriteAnalysisData(IOpenTDReport db, double FDR, int? dataSetId = null)
        {
            var rawFiles = db.GetDataFiles();
            // Must match the result sets WriteAnalysisCollection announced, or its
            // spectrumIdentificationList_ref attributes point at lists that were never written.
            var populatedResultSets = this.GetPopulatedResultSets(db, FDR, dataSetId);

            if (dataSetId.HasValue)
            {
                this.WriteStartElement("AnalysisData");
                // hitId -> scans: a hit can match multiple scans, so SII ids and their refs include the scan.
                var hitScans = new Dictionary<int, List<int>>();
                foreach (var resultSet in populatedResultSets)
                {
                    //Write  SpectrumIdentificationList
                    this.WriteStartElement("SpectrumIdentificationList");
                    this.WriteAttributeString("id", $"SIL_{resultSet.Key}");
                    this.WriteAttributeString("name", $"{resultSet.Value}");

                    //Fragmentation Table
                    this.WriteStartElement("FragmentationTable");
                    this.WriteStartElement("Measure");
                    this.WriteAttributeString("id", $"m_mz_{resultSet.Key}");
                    this.WriteCVParam("MS:1001225", "product ion m/z");
                    this.WriteEndElement();

                    this.WriteStartElement("Measure");
                    this.WriteAttributeString("id", $"m_error_{resultSet.Key}");
                    this.WriteCVParam("MS:1001227", "product ion m/z error", unitRef: "PSI-MS", unitAccession: "MS:1000040", unitName: "m/z");
                    this.WriteEndElement();
                    this.WriteEndElement();

                    var hits = db.CreateBatchOfHitsWithIons(resultSet.Key, dataSetId.Value, FDR);
                    foreach (var scan in hits)
                    {
                        //start SpectrumIdentificationResult one for each spectraData (aka raw file)
                        this.WriteStartElement("SpectrumIdentificationResult");
                        this.WriteAttributeString("id", $"SIR_{resultSet.Key}_{dataSetId}_{scan.Key}");
                        //this.WriteAttributeString("name", $"{resultSet.Value}");
                        this.WriteAttributeString("spectraData_ref", $"SD_{dataSetId.Value}");
                        this.WriteAttributeString("spectrumID", $"controllerType = 0 controllerNumber = 1 scan = {scan.Key}");

                        foreach (var hit in hits[scan.Key])
                        {

                            if (!hitScans.ContainsKey(hit.Key)) hitScans[hit.Key] = new List<int>();
                            hitScans[hit.Key].Add(scan.Key);

                            this.WriteStartElement("SpectrumIdentificationItem");
                            this.WriteAttributeString("id", $"SII_Hit_{hit.Key}_{scan.Key}_{resultSet.Key}_{dataSetId.Value}");
                            this.WriteAttributeString("calculatedMassToCharge", String.Format("{0:f5}", hit.Value.TheoPreMass + 1.00728));
                            this.WriteAttributeString("chargeState", $"1");
                            this.WriteAttributeString("experimentalMassToCharge", String.Format("{0:f5}", hit.Value.ObsPreMass + 1.00728));
                            this.WriteAttributeString("peptide_ref", $"Chem_{hit.Value.ChemId}");
                            this.WriteAttributeString("rank", $"1");
                            this.WriteAttributeString("passThreshold", $"true");

                            foreach (var iso in hit.Value.IsoformId)
                            {
                                this.WriteStartElement("PeptideEvidenceRef");
                                this.WriteAttributeString("peptideEvidence_ref", $"PE_Chem_{hit.Value.ChemId}_ISO_{iso}");
                                this.WriteEndElement();
                            }
                            this.WriteStartElement("Fragmentation");
                            foreach (var charge in hit.Value.FragmentIons)
                            {
                                foreach (var type in charge.Value)
                                {
                                    IEnumerable<FragmentIon> fragArray;
                                    if (type.Key == "B" || type.Key == "C" || type.Key == "A" || type.Key == "A+")
                                    {
                                        fragArray = type.Value.OrderBy(x => x.IonNumber);
                                    }
                                    else
                                    {
                                        fragArray = type.Value.OrderByDescending(x => x.IonNumber);
                                    }

                                    this.WriteStartElement("IonType");
                                    this.WriteAttributeString("index", string.Join(" ", fragArray.Select(x => x.IonNumber).ToArray()));
                                    this.WriteAttributeString("charge", $"{charge.Key}");
                                    this.WriteStartElement("FragmentArray");
                                    this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => x.ObservedMz.ToString("f4")).ToArray()));
                                    this.WriteAttributeString("measure_ref", $"m_mz_{resultSet.Key}");
                                    this.WriteEndElement();

                                    this.WriteStartElement("FragmentArray");
                                    this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => (x.ObservedMz - x.TheoreticalMz).ToString("e4")).ToArray()));
                                    this.WriteAttributeString("measure_ref", $"m_error_{resultSet.Key}");
                                    this.WriteEndElement();

                                    this.WriteFragType(type.Key); // IonType: FragmentArray* then cvParam
                                    this.WriteEndElement();
                                }
                            }

                            this.WriteEndElement();//end fragmenation

                            //Make CV for these?
                            this.WriteCVParam("MS:1003126", "ProSight:spectral P-score", String.Format("{0:g4}", hit.Value.PScore));
                            this.WriteCVParam("MS:1003127", "ProSight:spectral E-value", String.Format("{0:g2}", hit.Value.EValue));
                            this.WriteCVParam("MS:1003128", "ProSight:spectral C-score", String.Format("{0:g4}", hit.Value.CScore));
                            this.WriteCVParam("MS:1003125", "ProSight:spectral Q-value", String.Format("{0:e4}", hit.Value.GlobalQValue));
                            
                            if (hit.Value.Cleavages.HasValue)
                                this.WriteUserParam("Percentage of Inter-Residue Cleavages Observed", String.Format("{0:p0}", hit.Value.Cleavages.Value)); // is there a CV for this?


                            this.WriteEndElement();
                        }

                        this.WriteEndElement();
                    }

                    this.WriteEndElement(); //end  SpectrumIdentificationList
                }

                // Single ProteinDetectionList spanning all result sets (schema allows only one).
                // A result set with no hits has no protein hypotheses either, and its SII ids were
                // never written for SpectrumIdentificationItemRef to point at, so skip the query.
                this.WriteStartElement("ProteinDetectionList");
                this.WriteAttributeString("id", "PDL_1");
                foreach (var resultSet in populatedResultSets)
                {
                    var isoforms = db.GetproteinDetectiondata(resultSet.Key, dataSetId.Value, FDR);
                    this.WriteProteinAmbiguityGroups(isoforms, hitScans, resultSet.Key, dataSetId.Value);
                }
                this.WriteEndElement(); // end ProteinDetectionList

                this.WriteEndElement();
            }
            else
            {
                this.WriteStartElement("AnalysisData");
                // hitId -> scans: a hit can match multiple scans, so SII ids and their refs include the scan.
                var hitScans = new Dictionary<int, List<int>>();
                foreach (var resultSet in populatedResultSets)
                {
                    //Write  SpectrumIdentificationList
                    this.WriteStartElement("SpectrumIdentificationList");
                    this.WriteAttributeString("id", $"SIL_{resultSet.Key}");
                    this.WriteAttributeString("name", $"{resultSet.Value}");

                    //Fragmentation Table
                    this.WriteStartElement("FragmentationTable");
                    this.WriteStartElement("Measure");
                    this.WriteAttributeString("id", $"m_mz_{resultSet.Key}");
                    this.WriteCVParam("MS:1001225", "product ion m/z");
                    this.WriteEndElement();

                    this.WriteStartElement("Measure");
                    this.WriteAttributeString("id", $"m_error_{resultSet.Key}");
                    this.WriteCVParam("MS:1001227", "product ion m/z error", unitRef: "PSI-MS", unitAccession: "MS:1000040", unitName: "m/z");
                    this.WriteEndElement();
                    this.WriteEndElement();

                    foreach (var rawfile in rawFiles)
                    {

                        var hits = db.CreateBatchOfHitsWithIons(resultSet.Key, rawfile.Key, FDR);
                        foreach (var scan in hits)
                        {
                            //start SpectrumIdentificationResult one for each spectraData (aka raw file)
                            this.WriteStartElement("SpectrumIdentificationResult");
                            this.WriteAttributeString("id", $"SIR_{resultSet.Key}_{rawfile.Key}_{scan.Key}");
                            //this.WriteAttributeString("name", $"{resultSet.Value}");
                            this.WriteAttributeString("spectraData_ref", $"SD_{rawfile.Key}");
                            this.WriteAttributeString("spectrumID", $"controllerType = 0 controllerNumber = 1 scan = {scan.Key}");

                            foreach (var hit in hits[scan.Key])
                            {

                                if (!hitScans.ContainsKey(hit.Key)) hitScans[hit.Key] = new List<int>();
                                hitScans[hit.Key].Add(scan.Key);

                                this.WriteStartElement("SpectrumIdentificationItem");
                                this.WriteAttributeString("id", $"SII_Hit_{hit.Key}_{scan.Key}_{resultSet.Key}_{rawfile.Key}");
                                this.WriteAttributeString("calculatedMassToCharge", String.Format("{0:f5}", hit.Value.TheoPreMass + 1.00728));
                                this.WriteAttributeString("chargeState", $"1");
                                this.WriteAttributeString("experimentalMassToCharge", String.Format("{0:f5}", hit.Value.ObsPreMass + 1.00728));
                                this.WriteAttributeString("peptide_ref", $"Chem_{hit.Value.ChemId}");
                                this.WriteAttributeString("rank", $"1");
                                this.WriteAttributeString("passThreshold", $"true");

                                foreach (var iso in hit.Value.IsoformId)
                                {
                                    this.WriteStartElement("PeptideEvidenceRef");
                                    this.WriteAttributeString("peptideEvidence_ref", $"PE_Chem_{hit.Value.ChemId}_ISO_{iso}");
                                    this.WriteEndElement();
                                }

                                this.WriteStartElement("Fragmentation");
                                foreach (var charge in hit.Value.FragmentIons)
                                {
                                    foreach (var type in charge.Value)
                                    {
                                        IEnumerable<FragmentIon> fragArray;
                                        if (type.Key == "B" || type.Key == "C" || type.Key == "A")
                                        {
                                            fragArray = type.Value.OrderBy(x => x.IonNumber);
                                        }
                                        else
                                        {
                                            fragArray = type.Value.OrderByDescending(x => x.IonNumber);
                                        }

                                        this.WriteStartElement("IonType");
                                        this.WriteAttributeString("index", string.Join(" ", fragArray.Select(x => x.IonNumber).ToArray()));
                                        this.WriteAttributeString("charge", $"{charge.Key}");
                                        this.WriteStartElement("FragmentArray");
                                        this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => x.ObservedMz.ToString("f4")).ToArray()));
                                        this.WriteAttributeString("measure_ref", $"m_mz_{resultSet.Key}");
                                        this.WriteEndElement();

                                        this.WriteStartElement("FragmentArray");
                                        this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => (x.ObservedMz - x.TheoreticalMz).ToString("e4")).ToArray()));
                                        this.WriteAttributeString("measure_ref", $"m_error_{resultSet.Key}");
                                        this.WriteEndElement();

                                        this.WriteFragType(type.Key); // IonType: FragmentArray* then cvParam
                                        this.WriteEndElement();
                                    }
                                }

                                this.WriteEndElement();//end fragmenation

                                //Make CV for these?
                                this.WriteCVParam("MS:1003126", "ProSight:spectral P-score", String.Format("{0:g4}", hit.Value.PScore));
                                this.WriteCVParam("MS:1003127", "ProSight:spectral E-value", String.Format("{0:g2}", hit.Value.EValue));
                                this.WriteCVParam("MS:1003128", "ProSight:spectral C-score", String.Format("{0:g4}", hit.Value.CScore));
                                this.WriteCVParam("MS:1003125", "ProSight:spectral Q-value", String.Format("{0:e4}", hit.Value.GlobalQValue));

                                this.WriteUserParam("Percentage of Inter-Residue Cleavages Observed", String.Format("{0:p0}", hit.Value.Cleavages)); // is there a CV for this?


                                this.WriteEndElement();
                            }

                            //this.WriteAttributeString("spectrumID", $"controllerType = 0 controllerNumber = 1 scan = {}");

                            //List of all SpectrumIdentificationItems - Hits!

                            this.WriteEndElement();
                        }
                    }

                    this.WriteEndElement(); //end  SpectrumIdentificationList
                }

                // Single ProteinDetectionList spanning all result sets (schema allows only one).
                // A result set with no hits has no protein hypotheses either, and its SII ids were
                // never written for SpectrumIdentificationItemRef to point at, so skip the query.
                this.WriteStartElement("ProteinDetectionList");
                this.WriteAttributeString("id", "PDL_1");
                foreach (var resultSet in populatedResultSets)
                {
                    foreach (var rawfile in rawFiles)
                    {
                        var isoforms = db.GetproteinDetectiondata(resultSet.Key, rawfile.Key, FDR);
                        this.WriteProteinAmbiguityGroups(isoforms, hitScans, resultSet.Key, rawfile.Key);
                    }
                }
                this.WriteEndElement(); // end ProteinDetectionList

                this.WriteEndElement();
            }
        }

        /// <summary>
        /// Writes the ProteinAmbiguityGroups for one result set / raw file pair, leaving out anything
        /// the schema would reject as empty.
        ///
        /// GetproteinDetectiondata and the hits query do not filter identically - the protein query
        /// has neither the MS2-scan restriction nor the inner joins on HitScore - so it can return a
        /// hit that never received a SpectrumIdentificationItem to point at. PeptideHypothesis
        /// requires at least one SpectrumIdentificationItemRef, ProteinDetectionHypothesis at least
        /// one PeptideHypothesis, and ProteinAmbiguityGroup at least one of those, so dropping an
        /// unreferenced hit means dropping whatever it would leave empty above it.
        /// </summary>
        private void WriteProteinAmbiguityGroups(
            Dictionary<int, Dictionary<int, ProteinAmbiguityGroup>> isoforms,
            Dictionary<int, List<int>> hitScans,
            int resultSetId,
            int dataFileId)
        {
            foreach (var isoform in isoforms)
            {
                // Resolve the references first: whether any survive decides whether the enclosing
                // elements may be written at all.
                var refsByChem = new Dictionary<int, List<string>>();
                foreach (var chem in isoform.Value)
                {
                    var refs = new List<string>();
                    foreach (var hit in chem.Value.HitId)
                    {
                        if (!hitScans.TryGetValue(hit, out var scansForHit)) continue;
                        foreach (var scanNo in scansForHit)
                            refs.Add($"SII_Hit_{hit}_{scanNo}_{resultSetId}_{dataFileId}");
                    }

                    if (refs.Count > 0)
                        refsByChem[chem.Key] = refs;
                }

                if (refsByChem.Count == 0)
                    continue;

                //start ProteinAmbiguityGroup
                this.WriteStartElement("ProteinAmbiguityGroup");
                this.WriteAttributeString("id", $"PAG_{isoform.Key}_{resultSetId}_{dataFileId}");
                this.WriteStartElement("ProteinDetectionHypothesis");
                this.WriteAttributeString("id", $"PDH_{isoform.Key}_{resultSetId}_{dataFileId}");
                this.WriteAttributeString("dBSequence_ref", $"ISO_{isoform.Key}");
                this.WriteAttributeString("passThreshold", $"true");

                foreach (var chem in refsByChem)
                {
                    this.WriteStartElement("PeptideHypothesis");
                    this.WriteAttributeString("peptideEvidence_ref", $"PE_Chem_{chem.Key}_ISO_{isoform.Key}");
                    foreach (var itemRef in chem.Value)
                    {
                        this.WriteStartElement("SpectrumIdentificationItemRef");
                        this.WriteAttributeString("spectrumIdentificationItem_ref", itemRef);
                        this.WriteEndElement();
                    }
                    this.WriteEndElement();
                }

                // Both Q-values are protein-level, so any entry for this isoform carries the same pair.
                var proteinScores = isoform.Value.First().Value;
                this.WriteCVParam("MS:1003134", "ProSight:isoform Q-value", String.Format("{0:e4}", proteinScores.IsoformGlobalQvalue));
                this.WriteCVParam("MS:1003135", "ProSight:protein Q-value", String.Format("{0:e4}", proteinScores.EntryGlobalQValue));

                this.WriteEndElement(); //end ProteinDetectionHypothesis
                this.WriteEndElement(); //end ProteinAmbiguityGroup
            }
        }

        /// <summary>
        /// Writes the plus/minus pair for a FragmentTolerance or ParentTolerance element.
        ///
        /// ToleranceType requires at least one cvParam, so every case has to produce a pair. A value
        /// may be absent (ProSight PD stores no search parameters) or carry a unit this does not
        /// recognise; both fall back to -1 dalton, the convention already used for "not recorded".
        /// </summary>
        /// <summary>
        /// Whether a modification can be represented at all.
        ///
        /// Modification requires at least one cvParam and every cvParam requires a cvRef naming a
        /// declared cv, so a modification whose set id is missing has no valid form - emitting
        /// cvRef="" would only move the failure into the document. The set id is nullable
        /// independently of the mod id (it comes from a separate column), so checking the id alone
        /// is not enough. Reported rather than dropped quietly, since it means unexpected data.
        /// </summary>
        private static bool CanWriteModification(int? modId, string? modSetId, string context)
        {
            if (modId is null)
                return false;

            if (!string.IsNullOrEmpty(modSetId))
                return true;

            Console.WriteLine($"Skipping {context} modification {modId}: no modification set id, so it has no CV reference.");
            return false;
        }

        private void WriteToleranceParams(string? tolerance)
        {
            var text = (tolerance ?? "").Trim();

            if (text.EndsWith("ppm") && double.TryParse(text[..^3].Trim(), out var ppm))
            {
                this.WriteCVParam("MS:1001412", "search tolerance plus value", $"{ppm}", "UO", "UO:0000169", "parts per million");
                this.WriteCVParam("MS:1001413", "search tolerance minus value", $"{ppm}", "UO", "UO:0000169", "parts per million");
                return;
            }

            if (text.EndsWith("Da") && double.TryParse(text[..^2].Trim(), out var dalton))
            {
                this.WriteCVParam("MS:1001412", "search tolerance plus value", $"{dalton}", "UO", "UO:0000221", "dalton");
                this.WriteCVParam("MS:1001413", "search tolerance minus value", $"{dalton}", "UO", "UO:0000221", "dalton");
                return;
            }

            this.WriteCVParam("MS:1001412", "search tolerance plus value", "-1", "UO", "UO:0000221", "dalton");
            this.WriteCVParam("MS:1001413", "search tolerance minus value", "-1", "UO", "UO:0000221", "dalton");
        }

        private void WriteFragType(string fragType)
        {
            switch (fragType)
            {
                case "A":
                    {
                        this.WriteCVParam("MS:1001229", "frag: a ion");
                    }
                    break;
                case "B":
                    {
                        this.WriteCVParam("MS:1001224", "frag: b ion");
                    }
                    break;
                case "C":
                    {
                        this.WriteCVParam("MS:1001231", "frag: c ion");
                    }
                    break;

                case "X":
                    {
                        this.WriteCVParam("MS:1001228", "frag: x ion");
                    }
                    break;

                case "Y":
                    {
                        this.WriteCVParam("MS:1001220", "frag: y ion");
                    }
                    break;

                case "Z":
                    {
                        this.WriteCVParam("MS:1001230", "frag: z ion");
                    }
                    break;

                case "A+":
                    {
                        this.WriteCVParam("MS:1001229", "frag: a ion");
                    }
                    break;
                case "X+":
                    {
                        this.WriteCVParam("MS:1001228", "frag: x ion");
                    }
                    break;
                case "Y-":
                    {
                        this.WriteCVParam("MS:1001220", "frag: y ion");
                    }
                    break;


                case "Zdot":
                    {
                        this.WriteCVParam("MS:1001230", "frag: z ion");
                    }
                    break;

                default:
                    {
                        throw new Exception("Fragment Ion Type Could not be Parsed");
                    }
            }
        }

        private void WriteCVParam(string accession, string name, string value = "", string unitRef = "", string unitAccession = "", string unitName = "", string cvRef = "PSI-MS")
        {
            this.WriteStartElement("cvParam");
            this.WriteAttributeString("cvRef", cvRef);
            this.WriteAttributeString("accession", accession);
            this.WriteAttributeString("name", name);

            if (!string.IsNullOrEmpty(value))
                this.WriteAttributeString("value", value);

            if (!string.IsNullOrEmpty(unitRef))
            {
                this.WriteAttributeString("unitCvRef", unitRef);
                this.WriteAttributeString("unitAccession", unitAccession);
                this.WriteAttributeString("unitName", unitName);
            }

            this.WriteEndElement();
        }

        private void WriteUserParam(string name, string value = "", string type = "", string unitRef = "", string unitAccession = "", string unitName = "")
        {
            this.WriteStartElement("userParam");
            this.WriteAttributeString("name", name);

            if (!string.IsNullOrEmpty(type))
                this.WriteAttributeString("type", type);

            if (!string.IsNullOrEmpty(value))
                this.WriteAttributeString("value", value);

            if (!string.IsNullOrEmpty(unitRef))
            {
                this.WriteAttributeString("unitCvRef", unitRef);
                this.WriteAttributeString("unitAccession", unitAccession);
                this.WriteAttributeString("unitName", unitName);
            }

            this.WriteEndElement();
        }

        private void WriteMzIDStartElement(string name)
        {
            this.WriteStartElement("MzIdentML", "http://psidev.info/psi/pi/mzIdentML/1.1");
            this.WriteAttributeString("id", name);
            this.WriteAttributeString("version", "1.1.0");
            this.WriteAttributeString("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance", "http://psidev.info/psi/pi/mzIdentML/1.1 ../../schema/mzIdentML1.1.0.xsd");
            this.WriteAttributeString("creationDate", DateTime.Now.ToString("s")); // ISO-8601 xs:dateTime
        }

        private void WriteAnalysisSoftwareList(IOpenTDReport db)
        {
            bool isProSightPD = db.IsProSightPD;
            var software = _metadata.Software;
            string name = software?.Name ?? (isProSightPD ? "ProSight PD" : "TDPortal");
            string? version = software?.Version ?? db.SoftwareVersion;   // PD omits unless the JSON supplies it
            string uri = software?.Uri ?? DefaultSoftwareUri;

            this.WriteStartElement("AnalysisSoftwareList");
            this.WriteStartElement("AnalysisSoftware");
            this.WriteAttributeString("id", AnalysisSoftwareId(db));
            this.WriteAttributeString("name", name);
            if (!string.IsNullOrEmpty(version))
                this.WriteAttributeString("version", version);
            this.WriteAttributeString("uri", uri);

            this.WriteStartElement("SoftwareName");
            // CV term identifies the software family from provenance, even if name is overridden.
            if (isProSightPD)
                this.WriteCVParam("MS:1003141", "ProSight");
            else
                this.WriteCVParam("MS:1003142", "TDPortal");
            this.WriteEndElement(); //end SoftwareName

            this.WriteEndElement(); //end AnalysisSoftware
            this.WriteEndElement(); //end AnalysisSoftwareList
        }

        private void WriteAnalysisCollection(IOpenTDReport db, double FDR, int? dataFileId = null)
        {
            var rawFiles = db.GetDataFiles();
            var resultSets = db.GetResultSets();
            var massTable = db.GetMassTable();
            // Only the result sets that produce hits: each SpectrumIdentification here points at a
            // SpectrumIdentificationList that WriteAnalysisData has to fill with at least one result.
            var populatedResultSets = this.GetPopulatedResultSets(db, FDR, dataFileId);
            this.WriteStartElement("AnalysisCollection");
            foreach (var ResultSet in populatedResultSets)
            {
                //one for each result set?

                this.WriteStartElement("SpectrumIdentification");
                this.WriteAttributeString("id", $"SI_{ResultSet.Key}");
                this.WriteAttributeString("spectrumIdentificationProtocol_ref", $"SIP_{ResultSet.Key}");
                this.WriteAttributeString("spectrumIdentificationList_ref", $"SIL_{ResultSet.Key}");
                //this.WriteAttributeString("activityDate", $"{DateTime.Now}");


                // is this a list of all rawfiles?
                if (dataFileId.HasValue)
                {
                    this.WriteStartElement("InputSpectra");
                    this.WriteAttributeString("spectraData_ref", $"SD_{dataFileId}");
                    this.WriteEndElement();
                }
                else
                {
                    foreach (var raw in rawFiles)
                    {
                        this.WriteStartElement("InputSpectra");
                        this.WriteAttributeString("spectraData_ref", $"SD_{raw.Key}");
                        this.WriteEndElement();
                    }
                }

                this.WriteStartElement("SearchDatabaseRef");
                this.WriteAttributeString("searchDatabase_ref", "db1");
                this.WriteEndElement();

                this.WriteEndElement();
            }

            // Single ProteinDetection (schema allows one); reference every SIL that gets written.
            this.WriteStartElement("ProteinDetection");
            this.WriteAttributeString("id", "PD_1");
            this.WriteAttributeString("proteinDetectionProtocol_ref", "PDP_1");
            this.WriteAttributeString("proteinDetectionList_ref", "PDL_1");
            foreach (var ResultSet in populatedResultSets)
            {
                this.WriteStartElement("InputSpectrumIdentifications");
                this.WriteAttributeString("spectrumIdentificationList_ref", $"SIL_{ResultSet.Key}");
                this.WriteEndElement();
            }
            this.WriteEndElement();

            this.WriteEndElement();

            //AnalysisProtocolCollection
            this.WriteStartElement("AnalysisProtocolCollection");

            var parameters = db.GetParameters();
            // Deliberately every result set, not just the populated ones: a protocol records a search
            // that was run, which stays true when that search happened to identify nothing here. The
            // schema is happy with a protocol no SpectrumIdentification references.
            foreach (var ResultSet in resultSets)
            {
                this.WriteStartElement("SpectrumIdentificationProtocol");
                this.WriteAttributeString("id", $"SIP_{ResultSet.Key}");
                this.WriteAttributeString("name", $"{ResultSet.Value}");
                this.WriteAttributeString("analysisSoftware_ref", AnalysisSoftwareId(db));
                this.WriteStartElement("SearchType");

                this.WriteCVParam("MS:1001083", "ms-ms search");

                this.WriteEndElement();

                this.WriteStartElement("AdditionalSearchParams");
                var ResultSetParameters = db.GetResultSetParameters(ResultSet.Key);
                // AdditionalSearchParams must be non-empty; ProSight PD has no params, so always emit this.
                this.WriteUserParam("search mode", ResultSet.Value);

                // to do - CV params for - Annotated Proteoform Search mode, Subsequence Search mode,Run delta m mode
                if (ResultSet.Value == "BioMarker")
                {
                    this.WriteCVParam("MS:1003139", "ProSight:Run Subsequence Search mode", "True");
                }
                else if (ResultSet.Value == "Tight Absolute Mass")
                {
                    this.WriteCVParam("MS:1003140", "ProSight:Run Annotated Proteoform Search mode", "True");
                }
                if (ResultSetParameters.ContainsKey("delta_m"))
                {
                    if (ResultSetParameters["delta_m"] == "True")
                    {
                        this.WriteCVParam("MS:1003138", "ProSight:Run delta m mode", "True");
                    }
                    else
                    {
                        this.WriteCVParam("MS:1003138", "ProSight:Run delta m mode", "False");
                    }
                }

                foreach (var parameter in ResultSetParameters)
                {
                    if (parameter.Key != "fragment_tolerance" && parameter.Key != "precursor_window_tolerance" && parameter.Key != "delta_m")
                        this.WriteUserParam($"{ResultSet.Value} Parameter - {parameter.Key}", parameter.Value);
                }
                foreach (var parameterGroup in parameters)
                {
                    if (parameterGroup.Key != "Generate Report" && parameterGroup.Key != "Generate SAS Input")
                    {
                        foreach (var parameter in parameterGroup.Value)
                        {
                            this.WriteUserParam($"{parameterGroup.Key} - {parameter.Key}", parameter.Value);
                        }
                    }


                }

                this.WriteEndElement();

                this.WriteStartElement("MassTable");
                this.WriteAttributeString("id", $"MT_{ResultSet.Key}");
                this.WriteAttributeString("msLevel", "1 2");
                foreach (var aa in massTable)
                {
                    this.WriteStartElement("Residue");
                    this.WriteAttributeString("code", $"{aa.Key}");
                    this.WriteAttributeString("mass", $"{aa.Value}");
                    this.WriteEndElement();
                }

                //To Do add ambigous residues - what do the values mean!?
                this.WriteEndElement();

                this.WriteStartElement("FragmentTolerance");
                this.WriteToleranceParams(ResultSetParameters.GetValueOrDefault("fragment_tolerance"));
                this.WriteEndElement();

                this.WriteStartElement("ParentTolerance");
                this.WriteToleranceParams(ResultSetParameters.GetValueOrDefault("precursor_window_tolerance"));
                this.WriteEndElement();

                this.WriteStartElement("Threshold");

                this.WriteCVParam("MS:1002260", "PSM:FDR threshold", $"{FDR}");
                this.WriteCVParam("MS:1002910", "proteoform-level global FDR threshold", $"{FDR}");
                this.WriteCVParam("MS:1001448", "pep:FDR threshold", $"{FDR}");//do we need a proteoform level FDR CV?
                this.WriteEndElement();
                this.WriteEndElement();
            }

            // Single ProteinDetectionProtocol (schema allows one); its params are document-level.
            this.WriteStartElement("ProteinDetectionProtocol");
            this.WriteAttributeString("id", "PDP_1");
            this.WriteAttributeString("analysisSoftware_ref", AnalysisSoftwareId(db));
            this.WriteStartElement("AnalysisParams");

            bool wroteAnalysisParam = false;
            if (parameters.ContainsKey("Generate Report"))
            {
                foreach (var par in parameters["Generate Report"])
                {
                    this.WriteUserParam($"Generate Report - {par.Key}", par.Value);
                    wroteAnalysisParam = true;
                }
            }

            if (parameters.ContainsKey("Generate SAS Input"))
            {
                foreach (var par in parameters["Generate SAS Input"])
                {
                    this.WriteUserParam($"Generate SAS Input - {par.Key}", par.Value);
                    wroteAnalysisParam = true;
                }
            }

            // AnalysisParams requires >=1 child; ProSight PD reports carry no such parameters.
            if (!wroteAnalysisParam)
                this.WriteUserParam("protein detection parameters", "none");

            this.WriteEndElement();
            this.WriteStartElement("Threshold");
            this.WriteCVParam("MS:1001447", "prot:FDR threshold", $"{FDR}");
            this.WriteEndElement();
            this.WriteEndElement();

            this.WriteEndElement();
        }

        private void WriteSequenceCollection(IOpenTDReport db, double FDR, int? dataFileId = null)
        {
            this.WriteStartElement("SequenceCollection");
            var isofroms = db.GetDBSequences(FDR, dataFileId);

            foreach (var isform in isofroms)
            {
                WriteSingleDBSequence(isform.ID, isform.Accession, isform.Sequence, "db1", isform.Description, isform.TaxonID, isform.SciName);
            }

            var peptides = db.GetChemicalProteoforms(FDR, dataFileId);

            //Write Peptides - using BiologicalProteoformId as id
            foreach (var peptide in peptides)
            {
                this.WriteStartElement("Peptide");
                this.WriteAttributeString("id", $"Chem_{peptide.ID}");
                this.WriteStartElement("PeptideSequence");
                _writer.WriteString(peptide.Sequence);
                this.WriteEndElement();

                //C-Terminal Mods
                if (CanWriteModification(peptide.CterminalModID, peptide.CterminalModSetID, $"C-terminal on Chem_{peptide.ID}"))
                {
                    var Ctermmod = db.ModLookup(peptide.CterminalModID, peptide.CterminalModSetID, 0, 0);
                    this.WriteStartElement("Modification");
                    this.WriteAttributeString("location", $"{peptide.Sequence.Length + 1}");
                    this.WriteAttributeString("monoisotopicMassDelta", $"{Ctermmod.DiffMono}");
                    this.WriteAttributeString("avgMassDelta", $"{Ctermmod.DiffAverage}");
                    this.WriteCVParam($"{peptide.CterminalModSetID}:{peptide.CterminalModID}", Ctermmod.ModName, cvRef: peptide.CterminalModSetID!);
                    this.WriteEndElement();

                }
                //N-Terminal Mods
                if (CanWriteModification(peptide.NterminalModID, peptide.NterminalModSetID, $"N-terminal on Chem_{peptide.ID}"))
                {
                    var Ntermmod = db.ModLookup(peptide.NterminalModID, peptide.NterminalModSetID, 0, 0);
                    this.WriteStartElement("Modification");
                    this.WriteAttributeString("location", $"0");
                    this.WriteAttributeString("monoisotopicMassDelta", $"{Ntermmod.DiffMono}");
                    this.WriteAttributeString("avgMassDelta", $"{Ntermmod.DiffAverage}");
                    this.WriteCVParam($"{peptide.NterminalModSetID}:{peptide.NterminalModID}", Ntermmod.ModName, cvRef: peptide.NterminalModSetID!);
                    this.WriteEndElement();
                }
                //Add internal Mods
                if (peptide.ModificationHash != null)
                {
                    var pepmods = db.ParseModHash(peptide.ModificationHash, peptide.ID);
                    foreach (var pepmod in pepmods)
                    {
                        if (!CanWriteModification(pepmod.ModId, pepmod.ModSetId, $"residue on Chem_{peptide.ID}"))
                            continue;

                        this.WriteStartElement("Modification");
                        this.WriteAttributeString("location", $"{pepmod.StartIndex + 1}");
                        this.WriteAttributeString("monoisotopicMassDelta", $"{pepmod.DiffMono}");
                        this.WriteAttributeString("avgMassDelta", $"{pepmod.DiffAverage}");
                        // Omit the optional residues attr when absent (ProSight PD terminal mods) rather than emit residues="".
                        if (!string.IsNullOrEmpty(pepmod.AminoAcid))
                            this.WriteAttributeString("residues", pepmod.AminoAcid);
                        this.WriteCVParam($"{pepmod.ModSetId}:{pepmod.ModId}", pepmod.ModName, cvRef: pepmod.ModSetId!);
                        this.WriteEndElement();
                    }

                }

                //this.WriteEndElement();
                this.WriteEndElement();
            }
            var bioProforms = db.GetBiologicalProteoforms(FDR, dataFileId);
            foreach (var bioPForm in bioProforms)
            {
                char pre = '-';
                char post = '-';
                if (bioPForm.StartIndex != 0)
                {
                    pre = bioPForm.IsoformSeqence[bioPForm.StartIndex - 1];
                }

                if (bioPForm.EndIndex != bioPForm.Sequence.Length - 1)
                {
                    post = bioPForm.Sequence[bioPForm.Sequence.Length - 1];
                }

                this.WriteStartElement("PeptideEvidence");
                this.WriteAttributeString("id", $"PE_Chem_{bioPForm.ChemId}_ISO_{bioPForm.DBSequenceID}");
                this.WriteAttributeString("dBSequence_ref", $"ISO_{bioPForm.DBSequenceID}");
                this.WriteAttributeString("peptide_ref", $"Chem_{bioPForm.ChemId}");
                this.WriteAttributeString("start", $"{bioPForm.StartIndex}");
                this.WriteAttributeString("end", $"{bioPForm.EndIndex}");
                this.WriteAttributeString("pre", $"{pre}");
                this.WriteAttributeString("post", $"{post}");
                this.WriteAttributeString("isDecoy", $"false");

                // Only TDPortal bPFRs carry a proteoform-level (agg=1) Q-value; omit for cPFR-only ProSight PD.
                if (bioPForm.ProteoformQValue.HasValue)
                    this.WriteCVParam("MS:1003130", "ProSight:proteoform Q-value", String.Format("{0:e4}", bioPForm.ProteoformQValue.Value));
                this.WriteEndElement();
            }

            this.WriteEndElement();
        }

        private void WriteSingleDBSequence(int ID, string accession, string sequence, string searchDBRef, string proteinDescription, int taxID, string sciName)
        {
            this.WriteStartElement("DBSequence");
            this.WriteAttributeString("id", $"ISO_{ID}");
            this.WriteAttributeString("length", $"{sequence.Length}");
            this.WriteAttributeString("searchDatabase_ref", searchDBRef);
            this.WriteAttributeString("accession", accession);
            this.WriteStartElement("Seq");
            _writer.WriteString(sequence);
            this.WriteEndElement();
            this.WriteCVParam("MS:1001088", "protein description", proteinDescription);
            if (taxID > 0)
            {
                this.WriteCVParam("MS:1001469", "taxonomy: scientific name", sciName);
                this.WriteCVParam("MS:1001467", "taxonomy: NCBI TaxID", $"{taxID}");
            }

            this.WriteEndElement();
        }

        private void WriteProviderAndAuditCollection()
        {
            var submitter = _metadata.Submitter;
            string firstName = submitter?.FirstName ?? DefaultFirstName;
            string lastName = submitter?.LastName ?? DefaultLastName;
            string organization = submitter?.Organization ?? DefaultOrganization;
            string? email = submitter?.Email;
            string? organizationUri = submitter?.OrganizationUri;

            //provider section - who produced the document
            this.WriteStartElement("Provider");
            this.WriteAttributeString("id", "PROVIDER");

            this.WriteStartElement("ContactRole");
            this.WriteAttributeString("contact_ref", "PERSON_DOC_OWNER");
            this.WriteStartElement("Role");
            this.WriteCVParam("MS:1001271", "researcher");
            this.WriteEndElement(); //end Role
            this.WriteEndElement(); //end contact role
            this.WriteEndElement(); //end Provider

            //Audit Collection - the submitter and their organization (override via JSON metadata)
            this.WriteStartElement("AuditCollection");
            this.WriteStartElement("Person");
            this.WriteAttributeString("id", "PERSON_DOC_OWNER");
            this.WriteAttributeString("firstName", firstName);
            this.WriteAttributeString("lastName", lastName);
            if (!string.IsNullOrEmpty(email))
                this.WriteCVParam("MS:1000589", "contact email", email);
            this.WriteStartElement("Affiliation");
            this.WriteAttributeString("organization_ref", "ORG_DOC_OWNER");
            this.WriteEndElement(); //end Affiliation
            this.WriteEndElement(); //end person

            this.WriteStartElement("Organization");
            this.WriteAttributeString("id", "ORG_DOC_OWNER");
            this.WriteAttributeString("name", organization);
            if (!string.IsNullOrEmpty(organizationUri))
                this.WriteCVParam("MS:1000588", "contact URL", organizationUri);
            this.WriteEndElement(); //end Organization
            this.WriteEndElement(); //end AuditCollection
        }

        private void WriteMzIDCVList()
        {
            this.WriteStartElement("cvList"); // mzIdentML cvList has no 'count' attribute

            this.WriteStartElement("cv");
            this.WriteAttributeString("id", "PSI-MS");
            this.WriteAttributeString("fullName", "Proteomics Standards Initiative Mass Spectrometry Vocabularies");
            this.WriteAttributeString("version", "2.25.0");
            this.WriteAttributeString("uri", "http://psidev.cvs.sourceforge.net/viewvc/*checkout*/psidev/psi/psi-ms/mzML/controlledVocabulary/psi-ms.obo");
            this.WriteEndElement();


            this.WriteStartElement("cv");
            this.WriteAttributeString("id", "UNIMOD");
            this.WriteAttributeString("fullName", "UNIMOD");
            this.WriteAttributeString("version", "18:03:2011");
            this.WriteAttributeString("uri", "http://www.unimod.org/obo/unimod.obo");
            this.WriteEndElement();

            this.WriteStartElement("cv");
            this.WriteAttributeString("id", "PSI-MOD");
            this.WriteAttributeString("fullName", "Protein Modifications (PSI-MOD)");
            this.WriteAttributeString("uri", "http://purl.obolibrary.org/obo/mod.obo");
            this.WriteEndElement();

            this.WriteStartElement("cv");
            this.WriteAttributeString("id", "UO");
            this.WriteAttributeString("fullName", "UNIT-ONTOLOGY");
            this.WriteAttributeString("uri", "http://obo.cvs.sourceforge.net/*checkout*/obo/obo/ontology/phenotype/unit.obo");
            this.WriteEndElement();


            this.WriteEndElement();
        }
        private void WriteStartDoc()
        {
            _writer.WriteStartDocument();
        }
        private void WriteAttributeString(string localName, string value)
        {
            _writer.WriteAttributeString(localName, value);
        }
        private void WriteAttributeString(string prefix, string localName, string ns, string value)
        {
            _writer.WriteAttributeString(prefix, localName, ns, value);
        }
        private void WriteMzIDEndElement()
        {
            this.WriteEndElement();
        }
        private void WriteStartElement(string localName)
        {
            _writer.WriteStartElement(localName);
        }
        private void WriteStartElement(string localName, string ns)
        {
            _writer.WriteStartElement(localName, ns);
        }
        private void WriteElementString(string localName, string value)
        {
            _writer.WriteElementString(localName, value);
        }
        private void WriteEndElement()
        {
            _writer.WriteEndElement();
        }

        private void Flush() => _writer?.Flush();

        public void Dispose() => _writer?.Dispose();

        private static IOpenTDReport TDReportVersionCheck(string file, ReportSource source)
        {
            try
            {
                using (SqliteConnection connect = new(@$"Data Source={file}"))
                {
                    connect.Open();
                    using (SqliteCommand fmd = connect.CreateCommand())
                    {
                        fmd.CommandText = @"SELECT *  FROM Taxon";
                        fmd.CommandType = System.Data.CommandType.Text;
                        SqliteDataReader r = fmd.ExecuteReader();

                    }

                    Console.WriteLine("Found v3.1");
                    // v3.1 predates ProSight PD's tdReport export, so there is nothing to detect
                    // and nothing to override; say so rather than silently ignoring --source.
                    if (source != ReportSource.Auto)
                        Console.WriteLine("Note: --source is ignored for v3.1 reports (TDPortal only).");
                    return new OpenTDReport_31(file);
                }
            }
            catch
            {
                Console.WriteLine("Found v4.0");
                return new OpenTDReport_4(file, source);
            }
        }
    }
}