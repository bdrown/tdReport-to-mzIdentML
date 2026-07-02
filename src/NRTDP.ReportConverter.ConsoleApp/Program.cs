using System;
using System.Globalization;
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

            string tdReport = args[0];
            if (!File.Exists(tdReport))
            {
                Console.Error.WriteLine($"Error: tdReport file not found: {tdReport}");
                return 1;
            }

            string outputFolder = args.Length >= 2 ? args[1] : new FileInfo(tdReport).Directory.FullName;

            double fdr = 0.01;
            if (args.Length >= 3 && !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out fdr))
            {
                Console.Error.WriteLine($"Error: FDR must be a number (e.g. 0.01 for 1%); got: {args[2]}");
                return 1;
            }

            string metadataPath = args.Length >= 4 ? args[3] : null;
            if (metadataPath is not null && !File.Exists(metadataPath))
                Console.Error.WriteLine($"Warning: metadata file not found, using built-in defaults: {metadataPath}");

            try
            {
                Directory.CreateDirectory(outputFolder);
                var progress = new Progress<double>(pct => Console.WriteLine($"{(pct * 100):N2}"));
                MzidmlWriter.ConvertToSeperateMzId(tdReport, outputFolder, fdr, progress, MzidMetadata.Load(metadataPath));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: conversion failed: {ex.Message}");
                return 1;
            }
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
