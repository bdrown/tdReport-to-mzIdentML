using System;
using System.IO;

namespace NRTDP.tdReportConverter.ConsoleApp
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "/?")
            {
                PrintHelp();
                return args.Length == 0 ? 1 : 0;
            }

            if (args.Length > 4)
            {
                Console.Error.WriteLine("Error: too many arguments (expected 1-4).");
                Console.Error.WriteLine();
                PrintHelp();
                return 1;
            }

            var progress = new Progress<double>(pct => Console.WriteLine($"{(pct * 100):N2}"));

            string tdReport = args[0];
            string outputFolder = args.Length >= 2 ? args[1] : new FileInfo(tdReport).Directory.FullName;
            double fdr = args.Length >= 3 ? Convert.ToDouble(args[2]) : 0.01;
            MzidMetadata metadata = MzidMetadata.Load(args.Length >= 4 ? args[3] : null);

            MzidmlWriter.ConvertToSeperateMzId(tdReport, outputFolder, fdr, progress, metadata);
            return 0;
        }

        static void PrintHelp()
        {
            Console.WriteLine(
@"tdReport-to-mzIdentML - convert an NRTDP .tdReport into mzIdentML (.mzid).

Usage:
  tdReportConverter <tdReport> [outputFolder] [FDR] [metadataJson]

Arguments:
  tdReport       Path to the input .tdReport file (required).
  outputFolder   Where the .mzid files are written (default: the input file's folder).
  FDR            False discovery rate used to filter results (default: 0.01, i.e. 1%).
  metadataJson   Path to a JSON metadata-overrides file for fields the tdReport does not
                 contain - submitter, search database, software, and spectra format
                 (default: built-in defaults). See mzid-metadata.example.json.

One .mzid is written per raw file referenced by the report; percent-complete prints to stdout.

Examples:
  tdReportConverter report.tdReport
  tdReportConverter report.tdReport ./out 0.05 mzid-metadata.json

Options:
  -h, --help     Show this help and exit.");
        }
    }
}
