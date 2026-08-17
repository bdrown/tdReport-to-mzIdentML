using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;

namespace NRTDP.TDReport31
{
    internal class ReadTDReport_31 : DbContext
    {
        private string _tDReport;
        public ReadTDReport_31(string TDReport)
        {
            _tDReport = TDReport;

        }
        public DbSet<Hit> Hit { get; set; }
        public DbSet<HitScore> HitScore { get; set; }
        public DbSet<DecoyScore> DecoyScore { get; set; }
        public DbSet<LocalQualitativeConfidence> LocalQualitativeConfidence { get; set; }
        public DbSet<GlobalQualitativeConfidence> GlobalQualitativeConfidence { get; set; }
        public DbSet<ChemicalProteoform> ChemicalProteoform { get; set; }

        public new DbSet<Entry> Entry { get; set; } = null!;
        public DbSet<Isoform> Isoform { get; set; }

        public DbSet<BiologicalProteoform> BiologicalProteoform { get; set; }

        public DbSet<ResultSetToScoreType> ResultSetToScoreType { get; set; }
        public DbSet<Taxon> Taxon { get; set; }

        public DbSet<DataFile> DataFile { get; set; }

        public DbSet<ResultSet> ResultSet { get; set; }

        public DbSet<AminoAcid> AminoAcid { get; set; }

        public DbSet<ResultParameter> ResultParameter { get; set; }
        public DbSet<ChemicalProteoformFeature> ChemicalProteoformFeature { get; set; }
        public DbSet<Modification> Modification { get; set; }

        public DbSet<MatchingIon> MatchingIon { get; set; }
        public DbSet<HitToSpectrum> HitToSpectrum { get; set; }
        public DbSet<ScanHeaderToSpectrum> ScanHeaderToSpectrum { get; set; }
        public DbSet<ScanHeader> ScanHeader { get; set; }
        public DbSet<ScoreType> ScoreType { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
options.UseSqlite($"Data Source={_tDReport}") ;
            options.EnableDetailedErrors();
            options.EnableSensitiveDataLogging();
        }

                 





    }
    public class ScanHeader
    {
        public int Id { get; set; }
        public int ScanIndex { get; set; }
        public double RetentionTime { get; set; }
        public int DetectionType { get; set; }
        public int Level { get; set; }
        public double FragmentationMz { get; set; }
        public double FragmentationMzLowerOffset { get; set; }
        public double FragmentationMzUpperOffset { get; set; }

        // NULL for MS1 scans in real reports (2366 of 6424 in a ProSight PD report).
        public string? FragmentationMethodId { get; set; }

        public double FragmentationEnergy { get; set; }

        public double FragmentationReactionTime { get; set; }

        public int DataFileId { get; set; }



    }
    public class ScanHeaderToSpectrum
    {
        [Key]
        public int Id { get; set; }
        public int ScanHeaderId { get; set; }
        public int SpectrumId { get; set; }

    }
    public class HitToSpectrum
    {
        [Key]
        public int Id { get; set; }
        public int SpectrumId { get; set; }
        public int HitId { get; set; }

    }
    public class MatchingIon
    {
        [Key]
        public int Id { get; set; }
        public double ObservedMz { get; set; }
        public double TheoreticalMz { get; set; }
        public int Charge { get; set; }
        public int IonNumber { get; set; }
        public int CleavageSiteIndex { get; set; }
        public bool IsDeltaM { get; set; }
        public string IonTypeId { get; set; } = null!;
        public int HitId { get; set; }

    }
    public class Modification
    {
        [Key]
        public int Id { get; set; }
        public string ModificationSetId { get; set; } = null!;
        public string SwissprotTerm { get; set; } = null!;

        public string Name { get; set; } = null!;
        public string Definition { get; set; } = null!;
        public string Comment { get; set; } = null!;
        public double? DiffAverage { get; set; }
        public double? DiffMonoisotopic { get; set; }
        public string DiffFormula { get; set; } = null!;
        public string Formula { get; set; } = null!;
        public double MassAverage { get; set; }
        public double MassMonoisotopic { get; set; }
        public string AminoAcid { get; set; } = null!;
        public int? Source { get; set; }
        public int? Terminus { get; set; }
        public int? FormalCharge { get; set; }
        public bool IsObsolete { get; set; }
        public bool IsRemoval { get; set; }
        public bool IsSnp { get; set; }
        public int? ModificationTypeId { get; set; }


    }
    public class ChemicalProteoformFeature
    {
        [Key]
        public int Id { get; set; }
        public int Type { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public string ReplacementSequence { get; set; } = null!;
        public string OriginalType { get; set; } = null!;
        public string Description { get; set; } = null!;

        public string ModificationSetId { get; set; } = null!;
        public int ModificationId { get; set; }
        public double PriorWeight { get; set; }
        public int ChemicalProteoformId { get; set; }


    }

    public class ResultParameter
    {
        [Key]
        public int Id { get; set; }
        public string? GroupName { get; set; }
        public string  Name { get; set; } = null!;
        public string Value { get; set; } = null!;
        public int? ResultSetId { get; set; }

    }
    public class AminoAcid
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Symbol { get; set; } = null!;
        public string ExtendedSymbol { get; set; } = null!;
        public double MonoisotopicMass { get; set; }
        public double AverageMass { get; set; }
        public string Formula { get; set; } = null!;

    }

    public class Hit
    {
        public int Id { get; set; }
        public double ScoreForDecoy { get; set; }
        public double ObservedPrecursorMass { get; set; }
        public int ObservedPrecursorMassType { get; set; }
        public int ResultSetId { get; set; }
        public int DataFileId { get; set; }

        public int ChemicalProteoformId { get; set; }


    }

    public class HitScore
    {
        [Key]
        public int HitId { get; set; }

        public int ScoreTypeId { get; set; }
        public double? Value { get; set; }
    }
    public class DecoyScore
    {
        [Key]
        public int Id { get; set; }

        public double Score { get; set; }
        public int ResultSetId { get; set; }
        public int AggregationLevel { get; set; }
    }
    public class LocalQualitativeConfidence
    {
        [Key]
        public int Id { get; set; }
        public int AggregationLevel { get; set; }
        public int ExternalId { get; set; }
        public int HitId { get; set; }
        public double Pvalue { get; set; }
        public double Qvalue { get; set; }
        public double PvalueCharacterized { get; set; }
        public double QvalueCharacterized { get; set; }

    }

    public class ChemicalProteoform
    {
        [Key]
        public int Id { get; set; }
        public double MonoisotopicMass { get; set; }
        public double AverageMass { get; set; }
        public int? NTerminalModificationId { get; set; }
        public int? CTerminalModificationId { get; set; }
        public string? NTerminalModificationSetId { get; set; }
        public string? CTerminalModificationSetId { get; set; }
        public string? ModificationHash { get; set; }
        public string Sequence { get; set; } = null!;



    }
    public class Entry
    {
        [Key]
        public int Id { get; set; }
        public string UniProtId { get; set; } = null!;
        public string AccessionNumber { get; set; } = null!;
        public string Description { get; set; } = null!;
        public int TaxonId { get; set; }
        public double PriorWeight { get; set; }

    }

    public class GlobalQualitativeConfidence
    {
        [Key]
        public int Id { get; set; }
        public int AggregationLevel { get; set; }
        public int ExternalId { get; set; }
        public double GlobalQvalue { get; set; }
        public double GlobalQvalueCharacterized { get; set; }
        public int HitId { get; set; }

    }
    public class Isoform
    {
        [Key]
        public int Id { get; set; }
        public string AccessionNumber { get; set; } = null!;
        public string Description { get; set; } = null!;
        public bool IsSubsequence { get; set; }
        public double PriorWeight { get; set; }
        public string Sequence { get; set; } = null!;
        public int EntryId { get; set; }
    }
    public class BiologicalProteoform
    {
        [Key]
        public int Id { get; set; }
        public int ProteoformRecordNum { get; set; }
        public bool IsEndogenousCleavage { get; set; }
        public string Description { get; set; } = null!;
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public int IsoformId { get; set; }
        public int ChemicalProteoformId { get; set; }


    }
    public class ResultSetToScoreType
    {
        [Key]
        public int ResultSetId { get; set; }

        public int ScoreTypeId { get; set; }
        public int IsDefault { get; set; }
        public int IsVisible { get; set; }
    }
    public class Taxon
    {
        [Key]
        public int Id { get; set; }
        public string ScientificName { get; set; } = null!;
    }

    public class DataFile
    {
        [Key]
        public int ID { get; set; }
        public string Name { get; set; } = null!;
        public string FilePath { get; set; } = null!;

        public DateTime CreationDate { get; set; }
        public string Creator { get; set; } = null!;

        // NULL in real reports; the data-file description is often unset.
        public string? Description { get; set; }

    }

    public class ResultSet
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }

    }
    public class ScoreType
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string KeyWord { get; set; } = null!;
        public string Format { get; set; } = null!;
        public double BadValueRange { get; set; }
        public double GoodValueRange { get; set; }
        public int iSLogScale { get; set; }
        public int ScoreDirection { get; set; }

    }
}
