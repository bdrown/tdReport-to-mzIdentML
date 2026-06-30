using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;

namespace NRTDP.TDReport4
{
    internal class ReadTDReport_4 : DbContext
    {
        private string _tDReport;
        public ReadTDReport_4(string TDReport)
        {
            _tDReport = TDReport;
        }
        public DbSet<Hit> Hit { get; set; }
        public DbSet<HitScore> HitScore { get; set; }
        public DbSet<DecoyScore> DecoyScore { get; set; }
        public DbSet<LocalQualitativeConfidence> LocalQualitativeConfidence { get; set; }
        public DbSet<GlobalQualitativeConfidence> GlobalQualitativeConfidence { get; set; }
        public DbSet<ChemicalProteoform> ChemicalProteoform { get; set; }

        public DbSet<Entry> Entry { get; set; }
        public DbSet<Isoform> Isoform { get; set; }

        public DbSet<BiologicalProteoform> BiologicalProteoform { get; set; }

        public DbSet<ResultSetToScoreType> ResultSetToScoreType { get; set; }


        public DbSet<DataFile> DataFile { get; set; }

        public DbSet<ResultSet> ResultSet { get; set; }

        public DbSet<AminoAcid> AminoAcid { get; set; }

        public DbSet<ResultParameter> ResultParameter { get; set; }

        public DbSet<Modification> Modification { get; set; }

        public DbSet<MatchingIon> MatchingIon { get; set; }
        public DbSet<HitToSpectrum> HitToSpectrum { get; set; }
        public DbSet<ScanHeaderToSpectrum> ScanHeaderToSpectrum { get; set; }
        public DbSet<ScanHeader> ScanHeader { get; set; }
        public DbSet<ScoreType> ScoreType { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite($"Data Source={_tDReport}")
                   //.UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()))
                   ;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ResultParameter>().HasNoKey();
            modelBuilder.Entity<Modification>().HasNoKey();
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

        public string FragmentationMethodId { get; set; }

        public double FragmentationEnergy { get; set; }

        public double FragmentationReactionTime { get; set; }

        public int DataFileId { get; set; }



    }
    public class ScanHeaderToSpectrum
    {
        [Key]

        public int ScanHeaderId { get; set; }
        public int SpectrumId { get; set; }

    }
    public class HitToSpectrum
    {
        [Key]
        public int SpectrumId { get; set; }
        public int HitId { get; set; }

    }
    public class MatchingIon
    {
        [Key]

        public double ObservedMz { get; set; }
        public double TheoreticalMz { get; set; }
        public int Charge { get; set; }
        public int IonNumber { get; set; }
        public int CleavageSiteIndex { get; set; }
        public bool IsDeltaM { get; set; }
        public string IonTypeId { get; set; }
        public int HitId { get; set; }

    }
    public class Modification
    {
       
        public int Id { get; set; }
        public string ModificationSetId { get; set; }

        public int ModificationTypeId { get; set; }
        public string Name { get; set; }
        //public string Definition { get; set; }
        public double DiffAverage { get; set; }
        public double DiffMonoisotopic { get; set; }
        public string DiffFormula { get; set; }

        // Nullable: ProSight PD leaves Residues NULL for terminal mods; EF10 throws on NULL unless nullable.
        public string? Residues { get; set; }
        public int Terminus { get; set; }

    }


    public class ResultParameter
    {

        public string GroupName { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public string SearchName { get; set; }
        public int? ResultSetId { get; set; }

    }
    public class AminoAcid
    {
        [Key]

        public string Name { get; set; }
        public string Symbol { get; set; }
        public string ExtendedSymbol { get; set; }
        public double MonoisotopicMass { get; set; }
        public double AverageMass { get; set; }
        public string Formula { get; set; }

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

        // Nullable: some ProSight PD scores (e.g. cScore) are NULL. Must be nullable at the entity
        // level — EF10 optimizes away projection-side coalesces on non-nullable props and still throws.
        public double? Value { get; set; }
    }
    public class DecoyScore
    {
        [Key]

        public double Score { get; set; }
        public int ResultSetId { get; set; }
        public int AggregationLevel { get; set; }
    }
    public class LocalQualitativeConfidence
    {
        [Key]
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
        public string Sequence { get; set; }



    }
    public class Entry
    {
        [Key]
        public int Id { get; set; }
        public string UniProtId { get; set; }
        public string AccessionNumber { get; set; }
        public string Description { get; set; }
        public int TaxonId { get; set; }

    }

    public class GlobalQualitativeConfidence
    {
        [Key]
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
        public string AccessionNumber { get; set; }
        public string Description { get; set; }

        public string Sequence { get; set; }
        public int EntryId { get; set; }
    }
    public class BiologicalProteoform
    {
        [Key]
        public int Id { get; set; }
        public int ProteoformRecordNum { get; set; }

        public string Description { get; set; }
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


    public class DataFile
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }

        public DateTime CreationDate { get; set; }
        public string Creator { get; set; }

        public string Description { get; set; }

    }

    public class ResultSet
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }

    }

    public class ScoreType
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string KeyWord { get; set; }
        public string Format { get; set; }
        public double BadValueRange { get; set; }
        public double GoodValueRange { get; set; }
        public int iSLogScale { get; set; }
        public int ScoreDirection { get; set; }

    }

}