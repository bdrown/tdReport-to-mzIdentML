using System;
using System.CommandLine;
using System.IO;

namespace NRTDP.tdReportConverter.ConsoleApp
{
    class Program
    {
        static int Main(string[] args)
        {
            var tdReportArgument = new Argument<FileInfo>("tdReport")
            {
                Description = "Path to the input .tdReport file.",
            };
            tdReportArgument.AcceptExistingOnly();

            var outputFolderArgument = new Argument<DirectoryInfo?>("outputFolder")
            {
                Description = "Where the .mzid files are written (default: the input file's folder).",
                Arity = ArgumentArity.ZeroOrOne,
                DefaultValueFactory = _ => null,
            };

            var fdrOption = new Option<double>("--fdr")
            {
                Description = "False discovery rate used to filter results, as a fraction (e.g. 0.01 for 1%).",
                DefaultValueFactory = _ => 0.01,
            };
            fdrOption.Validators.Add(result =>
            {
                var value = result.GetValue(fdrOption);
                if (value is <= 0 or > 1)
                    result.AddError($"--fdr must be greater than 0 and at most 1 (a fraction, not a percentage); got {value}.");
            });

            var metadataOption = new Option<FileInfo>("--metadata", "-m")
            {
                Description = "JSON metadata-overrides file for fields the tdReport does not contain - "
                            + "submitter, search database, software, and spectra format. "
                            + "See mzid-metadata.example.json.",
            };
            metadataOption.AcceptExistingOnly();

            var sourceOption = new Option<string>("--source")
            {
                Description = "Which software produced the report. 'auto' infers it from the report's "
                            + "contents; the others override that inference.",
                DefaultValueFactory = _ => "auto",
            };
            sourceOption.AcceptOnlyFromAmong("auto", "tdportal", "prosight");

            var root = new RootCommand("Convert an NRTDP .tdReport into mzIdentML (.mzid). "
                                     + "One .mzid is written per raw file referenced by the report; "
                                     + "percent-complete prints to stdout.")
            {
                tdReportArgument,
                outputFolderArgument,
                fdrOption,
                metadataOption,
                sourceOption,
            };

            root.SetAction(parseResult =>
            {
                var tdReport = parseResult.GetValue(tdReportArgument)!;
                var outputFolder = parseResult.GetValue(outputFolderArgument);
                var fdr = parseResult.GetValue(fdrOption);
                var metadata = parseResult.GetValue(metadataOption);
                var source = parseResult.GetValue(sourceOption) switch
                {
                    "tdportal" => ReportSource.TDPortal,
                    "prosight" => ReportSource.ProSightPD,
                    _ => ReportSource.Auto,
                };

                var outputPath = outputFolder?.FullName ?? tdReport.Directory!.FullName;

                try
                {
                    Directory.CreateDirectory(outputPath);
                    var progress = new Progress<double>(pct => Console.WriteLine($"{(pct * 100):N2}"));
                    MzidmlWriter.ConvertToSeperateMzId(tdReport.FullName, outputPath, fdr, progress,
                                                       MzidMetadata.Load(metadata?.FullName), source);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: conversion failed: {ex.Message}");
                    return 1;
                }
            });

            return root.Parse(args).Invoke();
        }
    }
}
