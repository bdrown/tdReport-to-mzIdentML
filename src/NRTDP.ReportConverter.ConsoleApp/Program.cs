using System;
using System.IO;

namespace NRTDP.tdReportConverter.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            IProgress<double> progress = new Progress<double>(x => Console.WriteLine($"{(x * 100):N2}"));

            if ((args.Length == 1 && args[0] == "-h") || args.Length == 0)
                Console.WriteLine("Usage: <TDreportPath> <outputFolder (optional)> <FDR (optional defualt:0.01 i.e. 1%)> <metadataJson (optional)>");
            else if (args.Length == 1)
            {
                DirectoryInfo dir = (new FileInfo(args[0])).Directory;
                MzidmlWriter.ConvertToSeperateMzId(args[0], dir.FullName, 0.01, progress);
            }
            else if (args.Length == 2)
                MzidmlWriter.ConvertToSeperateMzId(args[0], args[1], 0.01, progress);
            else if (args.Length == 3)
                MzidmlWriter.ConvertToSeperateMzId(args[0], args[1], Convert.ToDouble(args[2]), progress);
            else if (args.Length == 4)
                MzidmlWriter.ConvertToSeperateMzId(args[0], args[1], Convert.ToDouble(args[2]), progress, MzidMetadata.Load(args[3]));
            else
                throw new ArgumentException("There must be between 1-4 arguments.");
        }
    }
}