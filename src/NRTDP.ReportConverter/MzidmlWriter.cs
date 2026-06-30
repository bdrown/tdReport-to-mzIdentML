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

        private MzidmlWriter(Stream stream, Encoding encoding)
        {
            _writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = encoding, Indent = true });
        }

        /// <summary>
        /// Converts a tdReport into compressed mzidml files. One for each raw file in the tdReport.
        /// </summary>
        /// <param name="TDReport">The file path for the tdReport</param>
        /// <param name="outputFolder">The output folder for the compressed mzidml files</param>
        /// <param name="FDR">The False Discovery Rate (FDR) used to filter the results</param>
        public static void ConvertToSeperateCompressedMzId(string TDReport, string outputFolder, double FDR = 0.05)
        {
            string tempFilePath = Path.GetTempFileName();

            var inputFileInfo = new FileInfo(TDReport);

            var _db = TDReportVersionCheck(inputFileInfo.FullName);

            var datasets = _db.GetDataFiles();
            double count = 0.0;
            foreach (var dataset in datasets)
            {
                var rawFileName = dataset.Value.Item1;

                var outputPath = Path.Join(outputFolder, $"{Path.GetFileNameWithoutExtension(rawFileName)}.mzid.gz");

                // Write the opening and short xml with a single stream 
                using (FileStream stream = File.Create(tempFilePath))
                using (MzidmlWriter writer = new(stream, Encoding.ASCII))
                {
                    writer.WriteStartDoc();
                    writer.WriteMzIDStartElement(inputFileInfo.Name);
                    writer.WriteMzIDCVList();
                    writer.WriteAnalysisSoftwareList(_db);
                    writer.WriteProviderAndAuditCollection();
                    writer.WriteSequenceCollection(_db, FDR, dataset.Key);
                    writer.WriteAnalysisCollection(_db, FDR, dataset.Key);
                    writer.WriteDataCollection(_db, inputFileInfo, FDR, dataset.Key);
                }

                using (FileStream sourcefs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                using (FileStream fs = new FileStream(outputPath, FileMode.Create))
                {
                    byte[] bytes = new byte[sourcefs.Length];
                    int numBytesToRead = (int)sourcefs.Length;
                    int numBytesRead = 0;
                    while (numBytesToRead > 0)
                    {
                        // Read may return anything from 0 to numBytesToRead.
                        int n = sourcefs.Read(bytes, numBytesRead, numBytesToRead);

                        // Break when the end of the file is reached.
                        if (n == 0)
                            break;

                        numBytesRead += n;
                        numBytesToRead -= n;
                    }
                    numBytesToRead = bytes.Length;

                    using (var compressionStream = new GZipStream(fs, CompressionMode.Compress))
                    {
                        compressionStream.Write(bytes, 0, bytes.Length);
                        compressionStream.Flush();
                    }
                }

                File.Delete(tempFilePath);
                count++;
                Console.WriteLine(count / datasets.Count());
            }
            _db.Dispose();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="TDReport">The file path for the tdReport</param>
        /// <param name="outputFolder">The output file for the mzidml file (include the .mzidml)</param>
        /// <param name="FDR">The False Discovery Rate (FDR) used to filter the results</param>
        public static void ConvertToSingleMzId(string TDReport, string outputPath, double FDR = 0.05)
        {
            var inputFileInfo = new FileInfo(TDReport);
            var _db = TDReportVersionCheck(inputFileInfo.FullName);

            using (FileStream stream = File.Create(outputPath))
            using (MzidmlWriter writer = new MzidmlWriter(stream, Encoding.ASCII))
            {
                writer.WriteStartDoc();
                writer.WriteMzIDStartElement(inputFileInfo.Name);
                writer.WriteMzIDCVList();
                writer.WriteAnalysisSoftwareList(_db);
                writer.WriteProviderAndAuditCollection();
                writer.WriteSequenceCollection(_db, FDR);
                writer.WriteAnalysisCollection(_db, FDR);
                writer.WriteDataCollection(_db, inputFileInfo, FDR);
            }
            _db.Dispose();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="TDReport">The file path for the tdReport</param>
        /// <param name="outputFolder">The output folder for the compressed mzidml files</param>
        /// <param name="FDR">The False Discovery Rate (FDR) used to filter the results</param>
        public static void ConvertToSeperateMzId(string TDReport, string outputFolder, double FDR = 0.05, IProgress<double>? progress = null)
        {
            var inputFileInfo = new FileInfo(TDReport);

            var _db = TDReportVersionCheck(inputFileInfo.FullName);

            var datasets = _db.GetDataFiles();
            int count = 0;
            foreach (var dataset in datasets)
            {
                var rawFileName = dataset.Value.Item1;

                var outputPath = Path.Join(outputFolder, $"{Path.GetFileNameWithoutExtension(rawFileName)}.mzid");

                using (FileStream stream = File.Create(outputPath))
                using (MzidmlWriter writer = new MzidmlWriter(stream, Encoding.ASCII))
                {
                    writer.WriteStartDoc();
                    writer.WriteMzIDStartElement(inputFileInfo.Name);
                    writer.WriteMzIDCVList();
                    writer.WriteAnalysisSoftwareList(_db);
                    writer.WriteProviderAndAuditCollection();
                    writer.WriteSequenceCollection(_db, FDR, dataset.Key);
                    writer.WriteAnalysisCollection(_db, FDR, dataset.Key);
                    writer.WriteDataCollection(_db, inputFileInfo, FDR, dataset.Key);
                }
                progress?.Report((double)++count / datasets.Count);
            }
            _db.Dispose();
        }

        private void WriteDataCollection(IOpenTDReport db, FileInfo inputFileInfo, double FDR, int? dataSetId = null)
        {
            this.WriteStartElement("DataCollection");
            this.WriteStartElement("Inputs");
            this.WriteStartElement("SourceFile");
            this.WriteAttributeString("location", $@"{inputFileInfo.FullName}");
            this.WriteAttributeString("id", "SF_1"); // TODO: optional Add FileFormat - needs CVparam for TDReport File
            this.WriteEndElement();

            this.WriteStartElement("SearchDatabase"); //Fake IT!
            this.WriteAttributeString("location", "C:/123FakeSt");
            this.WriteAttributeString("id", "db1");
            this.WriteStartElement("DatabaseName");
            this.WriteUserParam("Database1");
            this.WriteEndElement();
            this.WriteEndElement();

            //SpectraDAta
            var dataFiles = db.GetDataFiles();
            if (dataSetId.HasValue)
            {
                this.WriteStartElement("SpectraData");
                this.WriteAttributeString("location", dataFiles[dataSetId.Value].Item2);
                this.WriteAttributeString("id", $"SD_{dataSetId.Value}");
                this.WriteAttributeString("name", $"SD_{dataFiles[dataSetId.Value].Item1}");
                this.WriteStartElement("FileFormat");
                this.WriteCVParam("MS:1000563", "Thermo RAW format");
                this.WriteEndElement();

                this.WriteStartElement("SpectrumIDFormat");
                this.WriteCVParam("MS:1000768", "Thermo nativeID format");
                this.WriteEndElement();

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
                    this.WriteStartElement("FileFormat");
                    this.WriteCVParam("MS:1000563", "Thermo RAW format");
                    this.WriteEndElement();

                    this.WriteStartElement("SpectrumIDFormat");
                    this.WriteCVParam("MS:1000768", "Thermo nativeID format");
                    this.WriteEndElement();

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
            var resultSets = db.GetResultSets();

            if (dataSetId.HasValue)
            {
                this.WriteStartElement("AnalysisData");
                foreach (var resultSet in resultSets)
                {
                    //Write  SpectrumIdentificationList
                    this.WriteStartElement("SpectrumIdentificationList");
                    this.WriteAttributeString("id", $"SIL_{resultSet.Key}");
                    this.WriteAttributeString("name", $"{resultSet.Value}");

                    //Fragmentation Table
                    this.WriteStartElement("FragmentationTable");
                    this.WriteStartElement("Measure");
                    this.WriteAttributeString("id", "m_mz");
                    this.WriteCVParam("MS:1001225", "product ion m/z");
                    this.WriteEndElement();

                    this.WriteStartElement("m_error");
                    this.WriteAttributeString("id", "m_error");
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

                            this.WriteStartElement("SpectrumIdentificationItem");
                            this.WriteAttributeString("id", $"SII_Hit_{hit.Key}_{resultSet.Key}_{dataSetId.Value}");
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
                                    this.WriteFragType(type.Key);
                                    this.WriteStartElement("FragmentArray");
                                    this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => x.ObservedMz.ToString("f4")).ToArray()));
                                    this.WriteAttributeString("measure_ref", "m_mz");
                                    this.WriteEndElement();

                                    this.WriteStartElement("FragmentArray");
                                    this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => (x.ObservedMz - x.TheoreticalMz).ToString("e4")).ToArray()));
                                    this.WriteAttributeString("measure_ref", "m_error");
                                    this.WriteEndElement();

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

                foreach (var resultSet in resultSets)
                {
                    //Write  SpectrumIdentificationList
                    this.WriteStartElement("ProteinDetectionList");
                    this.WriteAttributeString("id", $"PDL_{resultSet.Key}");
                    this.WriteAttributeString("name", $"{resultSet.Value}");

                    var isoforms = db.GetproteinDetectiondata(resultSet.Key, dataSetId.Value, FDR);
                    foreach (var isoform in isoforms)
                    {
                        //start ProteinAmbiguityGroup
                        this.WriteStartElement("ProteinAmbiguityGroup");
                        this.WriteAttributeString("id", $"PAG_{isoform.Key}_{resultSet.Key}_{dataSetId.Value}"); // set to isform maybe entry!?
                        this.WriteStartElement("ProteinDetectionHypothesis");
                        this.WriteAttributeString("id", $"PDH_{isoform.Key}_{resultSet.Key}_{dataSetId.Value}");
                        this.WriteAttributeString("dBSequence_ref", $"ISO_{isoform.Key}");
                        this.WriteAttributeString("passThreshold", $"true");

                        foreach (var chem in isoforms[isoform.Key])
                        {
                            this.WriteStartElement("PeptideHypothesis");
                            this.WriteAttributeString("peptideEvidence_ref", $"PE_Chem_{chem.Key}_ISO_{isoform.Key}");
                            foreach (var hit in chem.Value.HitId)
                            {
                                this.WriteStartElement("SpectrumIdentificationItemRef");
                                this.WriteAttributeString("spectrumIdentificationItem_ref", $"SII_Hit_{hit}_{resultSet.Key}_{dataSetId.Value}");
                                this.WriteEndElement();
                            }

                            this.WriteEndElement();
                        }

                        this.WriteCVParam("MS:1003134", "ProSight:isoform Q-value", String.Format("{0:e4}", isoforms[isoform.Key].FirstOrDefault().Value.IsoformGlobalQvalue));
                        this.WriteCVParam("MS:1003135", "ProSight:protein Q-value", String.Format("{0:e4}", isoforms[isoform.Key].FirstOrDefault().Value.EntryGlobalQValue));

                        this.WriteEndElement();
                        this.WriteEndElement();
                    }

                    this.WriteEndElement();
                }

                this.WriteEndElement();
            }
            else
            {
                this.WriteStartElement("AnalysisData");
                foreach (var resultSet in resultSets)
                {
                    //Write  SpectrumIdentificationList
                    this.WriteStartElement("SpectrumIdentificationList");
                    this.WriteAttributeString("id", $"SIL_{resultSet.Key}");
                    this.WriteAttributeString("name", $"{resultSet.Value}");

                    //Fragmentation Table
                    this.WriteStartElement("FragmentationTable");
                    this.WriteStartElement("Measure");
                    this.WriteAttributeString("id", "m_mz");
                    this.WriteCVParam("MS:1001225", "product ion m/z");
                    this.WriteEndElement();

                    this.WriteStartElement("m_error");
                    this.WriteAttributeString("id", "m_error");
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

                                this.WriteStartElement("SpectrumIdentificationItem");
                                this.WriteAttributeString("id", $"SII_Hit_{hit.Key}_{resultSet.Key}_{rawfile.Key}");
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
                                        this.WriteFragType(type.Key);
                                        this.WriteStartElement("FragmentArray");
                                        this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => x.ObservedMz.ToString("f4")).ToArray()));
                                        this.WriteAttributeString("measure_ref", "m_mz");
                                        this.WriteEndElement();

                                        this.WriteStartElement("FragmentArray");
                                        this.WriteAttributeString("values", string.Join(" ", fragArray.Select(x => (x.ObservedMz - x.TheoreticalMz).ToString("e4")).ToArray()));
                                        this.WriteAttributeString("measure_ref", "m_error");
                                        this.WriteEndElement();

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

                foreach (var resultSet in resultSets)
                {
                    //Write  SpectrumIdentificationList
                    this.WriteStartElement("ProteinDetectionList");
                    this.WriteAttributeString("id", $"PDL_{resultSet.Key}");
                    this.WriteAttributeString("name", $"{resultSet.Value}");


                    foreach (var rawfile in rawFiles)
                    {

                        var isoforms = db.GetproteinDetectiondata(resultSet.Key, rawfile.Key, FDR);
                        foreach (var isoform in isoforms)
                        {
                            //start ProteinAmbiguityGroup
                            this.WriteStartElement("ProteinAmbiguityGroup");
                            this.WriteAttributeString("id", $"PAG_{isoform.Key}_{resultSet.Key}_{rawfile.Key}"); // set to isform maybe entry!?
                            this.WriteStartElement("ProteinDetectionHypothesis");
                            this.WriteAttributeString("id", $"PDH_{isoform.Key}_{resultSet.Key}_{rawfile.Key}");
                            this.WriteAttributeString("dBSequence_ref", $"ISO_{isoform.Key}");
                            this.WriteAttributeString("passThreshold", $"true");


                            foreach (var chem in isoforms[isoform.Key])
                            {
                                this.WriteStartElement("PeptideHypothesis");
                                this.WriteAttributeString("peptideEvidence_ref", $"PE_Chem_{chem.Key}_ISO_{isoform.Key}");
                                foreach (var hit in chem.Value.HitId)
                                {
                                    this.WriteStartElement("SpectrumIdentificationItemRef");
                                    this.WriteAttributeString("spectrumIdentificationItem_ref", $"SII_Hit_{hit}_{resultSet.Key}_{rawfile.Key}");
                                    this.WriteEndElement();
                                }

                                this.WriteEndElement();

                            }
                   
                            this.WriteCVParam("MS:1003134", "ProSight:isoform Q-value", String.Format("{0:e4}", isoforms[isoform.Key].FirstOrDefault().Value.IsoformGlobalQvalue));
                            this.WriteCVParam("MS: 1003135", "ProSight:protein Q-value", String.Format("{0:e4}", isoforms[isoform.Key].FirstOrDefault().Value.EntryGlobalQValue));

                            this.WriteEndElement();
                            this.WriteEndElement();
                        }
                    }

                    this.WriteEndElement();
                }

                this.WriteEndElement();
            }
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
            this.WriteStartElement("MzIdentML", "http://psi.hupo.org/ms/mzml");
            this.WriteAttributeString("id", name);
            this.WriteAttributeString("version", "1.1.0");
            this.WriteAttributeString("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance", "http://psidev.info/psi/pi/mzIdentML/1.1 ../../schema/mzIdentML1.1.0.xsd");
            this.WriteAttributeString("creationDate", $"{DateTime.Now}");
        }

        private void WriteAnalysisSoftwareList(IOpenTDReport db)
        {
            bool isProSightPD = db.IsProSightPD;

            this.WriteStartElement("AnalysisSoftwareList");
            this.WriteStartElement("AnalysisSoftware");
            // id stays "AS_TDPortal" (referenced by analysisSoftware_ref elsewhere); only name + CV term vary.
            this.WriteAttributeString("id", "AS_TDPortal");
            this.WriteAttributeString("name", isProSightPD ? "ProSight PD" : "TDPortal");
            if (!isProSightPD)
                this.WriteAttributeString("version", "4.0.0");
            this.WriteAttributeString("uri", "http://www.kelleher.northwestern.edu/");

            this.WriteStartElement("ContactRole");
            this.WriteAttributeString("contact_ref", "ORG_NU");
            this.WriteStartElement("Role");
            this.WriteCVParam("MS:1001267", "software vendor");
            this.WriteEndElement(); //end Role
            this.WriteEndElement(); //end contact role

            this.WriteStartElement("SoftwareName");

            if (isProSightPD)
                this.WriteCVParam("MS:1003141", "ProSight");
            else
                this.WriteCVParam("MS:1003142", "TDPortal");

            this.WriteEndElement(); //end SoftwareName

            //TODO: Add Customizations? its optional Free text 
            //_writer.WriteString(Customizations);

            this.WriteEndElement(); //end AnalysisSoftware
            this.WriteEndElement(); //end AnalysisSoftwareList
        }

        private void WriteAnalysisCollection(IOpenTDReport db, double FDR, int? dataFileId = null)
        {
            var rawFiles = db.GetDataFiles();
            var resultSets = db.GetResultSets();
            var massTable = db.GetMassTable();
            this.WriteStartElement("AnalysisCollection");
            foreach (var ResultSet in resultSets)
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

            foreach (var ResultSet in resultSets)
            {
                this.WriteStartElement("ProteinDetection");
                this.WriteAttributeString("id", $"PD_{ResultSet.Key}");
                this.WriteAttributeString("proteinDetectionProtocol_ref", $"PDP_{ResultSet.Key}");
                this.WriteAttributeString("proteinDetectionList_ref", $"PDL_{ResultSet.Key}");
                //this.WriteAttributeString("activityDate", $"{DateTime.Now}");

                this.WriteStartElement("InputSpectrumIdentifications");
                this.WriteAttributeString("spectrumIdentificationList_ref", $"SIL_{ResultSet.Key}");
                this.WriteEndElement();

                this.WriteEndElement();
            }

            this.WriteEndElement();

            //AnalysisProtocolCollection
            this.WriteStartElement("AnalysisProtocolCollection");

            var parameters = db.GetParameters();
            //forEach SIP
            foreach (var ResultSet in resultSets)
            {
                this.WriteStartElement("SpectrumIdentificationProtocol");
                this.WriteAttributeString("id", $"SIP_{ResultSet.Key}");
                this.WriteAttributeString("name", $"{ResultSet.Value}");
                this.WriteAttributeString("analysisSoftware_ref", "AS_TDPortal");
                this.WriteStartElement("SearchType");

                this.WriteCVParam("MS:1001083", "ms-ms search");

                this.WriteEndElement();

                this.WriteStartElement("AdditionalSearchParams"); //What to put here!? do UserPArams for now
                var ResultSetParameters = db.GetResultSetParameters(ResultSet.Key);

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
                this.WriteAttributeString("id", $"MT");
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

                // ProSight PD has no fragment_tolerance (empty ResultParameter); guard + emit -1 like the precursor block below.
                if (ResultSetParameters.ContainsKey("fragment_tolerance"))
                {
                    if (ResultSetParameters["fragment_tolerance"].TrimEnd(null).EndsWith("ppm"))
                    {
                        var tol = Double.Parse(ResultSetParameters["fragment_tolerance"].Remove(ResultSetParameters["fragment_tolerance"].IndexOf('p'), 3));
                        this.WriteCVParam("MS:1001412", "search tolerance plus value", $"{ tol}", "UO", "UO:0000169", "parts per million");
                        this.WriteCVParam("MS:1001413", "search tolerance minus value", $"{ tol}", "UO", "UO:0000169", "parts per million");
                    }
                    else if (ResultSetParameters["fragment_tolerance"].TrimEnd(null).EndsWith("Da"))
                    {
                        var tol = Double.Parse(ResultSetParameters["fragment_tolerance"].Remove(ResultSetParameters["fragment_tolerance"].LastIndexOf('D'), 2));
                        this.WriteCVParam("MS:1001412", "search tolerance plus value", $"{ tol}", "UO", "UO:0000221", "dalton");
                        this.WriteCVParam("MS:1001413", "search tolerance minus value", $"{ tol}", "UO", "UO:0000221", "dalton");
                    }
                }
                else
                {
                    this.WriteCVParam("MS:1001412", "search tolerance plus value", $"-1", "UO", "UO:0000221", "dalton");
                    this.WriteCVParam("MS:1001413", "search tolerance minus value", $"-1", "UO", "UO:0000221", "dalton");
                }

                this.WriteEndElement();


                this.WriteStartElement("precursor_window_tolerance");
                if (ResultSetParameters.ContainsKey("precursor_window_tolerance"))
                {
                    if (ResultSetParameters["precursor_window_tolerance"].TrimEnd(null).EndsWith("ppm"))
                    {

                        var tol = Double.Parse(ResultSetParameters["precursor_window_tolerance"].Remove(ResultSetParameters["precursor_window_tolerance"].IndexOf('p'), 3));
                        this.WriteCVParam("MS:1001412", "search tolerance plus value", $"{ tol}", "UO", "UO:0000169", "parts per million");
                        this.WriteCVParam("MS:1001413", "search tolerance minus value", $"{ tol}", "UO", "UO:0000169", "parts per million");
                    }
                    else if (ResultSetParameters["precursor_window_tolerance"].TrimEnd(null).EndsWith("Da"))
                    {
                        var tol = Double.Parse(ResultSetParameters["precursor_window_tolerance"].Remove(ResultSetParameters["precursor_window_tolerance"].LastIndexOf('D'), 2));
                        this.WriteCVParam("MS:1001412", "search tolerance plus value", $"{ tol}", "UO", "UO:0000221", "dalton");
                        this.WriteCVParam("MS:1001413", "search tolerance minus value", $"{ tol}", "UO", "UO:0000221", "dalton");
                    }
                }
                else
                {
                    this.WriteCVParam("MS:1001412", "search tolerance plus value", $"-1", "UO", "UO:0000221", "dalton");
                    this.WriteCVParam("MS:1001413", "search tolerance minus value", $"-1", "UO", "UO:0000221", "dalton");
                }

                this.WriteEndElement();

                this.WriteStartElement("Threshold");

                this.WriteCVParam("MS:1002260", "PSM:FDR threshold", $"{FDR}");
                this.WriteCVParam("MS:1002910", "proteoform-level global FDR threshold", $"{FDR}");
                this.WriteCVParam("MS:1001448", "pep:FDR threshold", $"{FDR}");//do we need a proteoform level FDR CV?
                this.WriteEndElement();
                this.WriteEndElement();
            }

            //ForEach PDP
            foreach (var ResultSet in resultSets)
            {
                this.WriteStartElement("ProteinDetectionProtocol");
                this.WriteAttributeString("id", $"PDP_{ResultSet.Key}");

                this.WriteAttributeString("analysisSoftware_ref", "AS_TDPortal");
                this.WriteStartElement("AnalysisParams");

                //protein determination parameters and report generation?

                if (parameters.ContainsKey("Generate Report"))
                {
                    foreach (var par in parameters["Generate Report"])
                    {
                        this.WriteUserParam($"Generate Report - {par.Key}", par.Value);
                    }
                }

                if (parameters.ContainsKey("Generate SAS Input"))
                {
                    foreach (var par in parameters["Generate SAS Input"])
                    {
                        this.WriteUserParam($"Generate SAS Input - {par.Key}", par.Value);

                    }
                }

                this.WriteEndElement();
                this.WriteStartElement("Threshold");
                this.WriteCVParam("MS:1001447", "prot:FDR threshold", $"{FDR}"); //do we need a proteoform level FDR CV?
                this.WriteEndElement();
                this.WriteEndElement();

            }

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
                if (peptide.CterminalModID != null)
                {
                    var Ctermmod = db.ModLookup(peptide.CterminalModID, peptide.CterminalModSetID, 0, 0);
                    this.WriteStartElement("Modification");
                    this.WriteAttributeString("location", $"{peptide.Sequence.Length + 1}");
                    this.WriteAttributeString("monoisotopicMassDelta", $"{Ctermmod.DiffMono}");
                    this.WriteAttributeString("avgMassDelta", $"{Ctermmod.DiffAverage}");
                    this.WriteCVParam($"{peptide.CterminalModSetID}:{peptide.CterminalModID}", Ctermmod.ModName, cvRef: peptide.CterminalModSetID);
                    this.WriteEndElement();

                }
                //N-Terminal Mods
                if (peptide.NterminalModID != null)
                {
                    var Ntermmod = db.ModLookup(peptide.NterminalModID, peptide.NterminalModSetID, 0, 0);
                    this.WriteStartElement("Modification");
                    this.WriteAttributeString("location", $"0");
                    this.WriteAttributeString("monoisotopicMassDelta", $"{Ntermmod.DiffMono}");
                    this.WriteAttributeString("avgMassDelta", $"{Ntermmod.DiffAverage}");
                    this.WriteCVParam($"{peptide.NterminalModSetID}:{peptide.NterminalModID}", Ntermmod.ModName, cvRef: peptide.NterminalModSetID);
                    this.WriteEndElement();
                }
                //Add internal Mods
                if (peptide.ModificationHash != null)
                {
                    var pepmods = db.ParseModHash(peptide.ModificationHash, peptide.ID);
                    foreach (var pepmod in pepmods)
                    {
                        this.WriteStartElement("Modification");
                        this.WriteAttributeString("location", $"{pepmod.StartIndex + 1}");
                        this.WriteAttributeString("monoisotopicMassDelta", $"{pepmod.DiffMono}");
                        this.WriteAttributeString("avgMassDelta", $"{pepmod.DiffAverage}");
                        // Omit the optional residues attr when absent (ProSight PD terminal mods) rather than emit residues="".
                        if (!string.IsNullOrEmpty(pepmod.AminoAcid))
                            this.WriteAttributeString("residues", pepmod.AminoAcid);
                        this.WriteCVParam($"{pepmod.ModSetId}:{pepmod.ModId}", pepmod.ModName, cvRef: pepmod.ModSetId);
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

                this.WriteCVParam("MS:1003130", "ProSight:proteoform Q-value", String.Format("{0:e4}", bioPForm.ProteoformQValue));
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
            //provider section
            this.WriteStartElement("Provider");
            this.WriteAttributeString("id", "PROVIDER");

            this.WriteStartElement("ContactRole");
            this.WriteAttributeString("contact_ref", "PERSON_DOC_OWNER");
            this.WriteStartElement("Role");
            this.WriteCVParam("MS:1001271", "researcher");
            this.WriteEndElement(); //end Role
            this.WriteEndElement(); //end contact role
            this.WriteEndElement(); //end Provider

            //Audit Collection - Could take user name - stick with Neil for now
            this.WriteStartElement("AuditCollection");
            this.WriteStartElement("Person");
            this.WriteAttributeString("id", "");
            this.WriteStartElement("Affiliation");
            this.WriteAttributeString("organization_ref", "ORG_NU");
            this.WriteEndElement(); //end 
            this.WriteEndElement(); //end person

            this.WriteStartElement("Person");
            this.WriteAttributeString("id", "PERSON_DOC_OWNER");
            this.WriteAttributeString("firstName", "Neil");
            this.WriteAttributeString("lastName", "Kelleher");
            this.WriteStartElement("Affiliation");
            this.WriteAttributeString("organization_ref", "ORG_DOC_OWNER");
            this.WriteEndElement();
            this.WriteEndElement(); //end person

            this.WriteStartElement("Organization");
            this.WriteAttributeString("id", "ORG_NU");
            this.WriteAttributeString("name", "NorthWestern University");
            this.WriteEndElement(); //end Organization

            this.WriteStartElement("Organization");
            this.WriteAttributeString("id", "ORG_DOC_OWNER");

            this.WriteEndElement(); //end Organization
            this.WriteEndElement(); //end AuditCollection
        }

        private void WriteMzIDCVList()
        {
            this.WriteStartElement("cvList");
            this.WriteAttributeString("count", "3");

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

        private static IOpenTDReport TDReportVersionCheck(string file)
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
                    return new OpenTDReport_31(file);
                }
            }
            catch
            {
                Console.WriteLine("Found v4.0");
                return new OpenTDReport_4(file);
            }
        }
    }
}