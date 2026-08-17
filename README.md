# tdReport-to-mzIdentML

Convert NRTDP `.tdReport` files into [mzIdentML](https://www.psidev.info/mzidentml)
(`.mzid`) so top‑down proteomics identifications can be deposited to
[ProteomeXchange](https://www.proteomexchange.org/) / [PRIDE](https://www.ebi.ac.uk/pride/).

A `.tdReport` is a SQLite database produced by NRTDP tooling. This project reads it and
writes a schema‑valid **mzIdentML 1.1.0** document per raw file.

## Features

- **Both producers supported:** TDPortal and **ProSight PD** reports (tdReport schema v3.1 and v4.0).
- **Schema‑valid output:** validates against the official mzIdentML 1.1.0 XSD.
- **One `.mzid` per raw file** in the report (or a single whole‑report document, or gzipped output).
- **FDR filtering** at conversion time.
- **Optional metadata overrides** (submitter, search database, software, spectra format) that the
  tdReport does not itself contain — supplied via a small JSON file.
- Ships as a reusable **NuGet library** (`NRTDP.tdReportConverter`) plus a thin console app.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the project targets `net10.0`).

## Build

```bash
dotnet restore src/NRTDP.ReportConverter.sln
dotnet build   src/NRTDP.ReportConverter.sln -c Release
```

## Usage (console app)

```
dotnet run --project src/NRTDP.ReportConverter.ConsoleApp -- <tdReport> [outputFolder] [options]
```

| Argument       | Required | Default                  | Description                          |
|----------------|----------|--------------------------|--------------------------------------|
| `tdReport`     | yes      | —                        | Path to the input `.tdReport` file.  |
| `outputFolder` | no       | folder of the input file | Where the `.mzid` files are written. |

| Option             | Default           | Description                                                     |
|--------------------|-------------------|-----------------------------------------------------------------|
| `--fdr`            | `0.01` (1%)       | False discovery rate used to filter results, as a fraction.     |
| `-m`, `--metadata` | built‑in defaults | Path to a metadata overrides file (see below).                  |
| `--source`         | `auto`            | Report producer: `auto`, `tdportal`, or `prosight` (see below). |

One `.mzid` is written per raw file referenced by the report; percent‑complete is printed to stdout.
Run with `--help` for the full list.

**Examples**

```bash
# Simplest: outputs next to the input, 1% FDR
dotnet run --project src/NRTDP.ReportConverter.ConsoleApp -- report.tdReport

# Custom output folder + 5% FDR + metadata overrides
dotnet run --project src/NRTDP.ReportConverter.ConsoleApp -- report.tdReport ./out --fdr 0.05 -m mzid-metadata.json
```

> **Note:** FDR and the metadata file used to be positional (`report.tdReport ./out 0.05 meta.json`).
> They are now the named options `--fdr` and `--metadata`; the two path arguments are unchanged.

## Report source detection

TDPortal and ProSight PD both write the same v4.0 tdReport schema, so the producer is inferred from
the report's contents: TDPortal stamps its assembly versions into the `DbMetadata` table
(`GenerateBatchedTargetPufDbHT`, `GenerateReportHT`, `pufdb_version`) while ProSight PD writes only
`reporting_version`, and correspondingly leaves the `ResultParameter` table empty. The detected
producer determines the `AnalysisSoftware` provenance written to the `.mzid` and which
TDPortal‑only values are read.

The converter prints what it detected. If it ever gets this wrong, override it:

```bash
dotnet run --project src/NRTDP.ReportConverter.ConsoleApp -- report.tdReport --source prosight
```

(v3.1 reports are always TDPortal, so `--source` does not apply to them.)

## Metadata overrides

Some mzIdentML fields aren't stored in a tdReport (who submitted the data, which sequence database
was searched, the software version, the raw‑file format). Provide them in a JSON file; every section
and field is optional and falls back to a built‑in default. See
[`mzid-metadata.example.json`](mzid-metadata.example.json) for the full shape:

```json
{
  "submitter":      { "firstName": "...", "lastName": "...", "email": "...", "organization": "..." },
  "software":       { "name": "...", "version": "...", "uri": "..." },
  "searchDatabase": { "name": "...", "version": "...", "location": "...", "taxonomy": "..." },
  "spectraData":    { "fileFormatAccession": "MS:1000563", "fileFormatName": "Thermo RAW format" }
}
```

## Library usage

```csharp
using NRTDP.tdReportConverter;

// One .mzid per raw file (what the console app uses)
MzidmlWriter.ConvertToSeperateMzId("report.tdReport", "./out", FDR: 0.01);

// A single whole-report document
MzidmlWriter.ConvertToSingleMzId("report.tdReport", "./out/report.mzid", FDR: 0.01);

// Gzipped per-raw-file output (.mzid.gz)
MzidmlWriter.ConvertToSeperateCompressedMzId("report.tdReport", "./out", FDR: 0.01);

// Override producer detection (equivalent of --source)
MzidmlWriter.ConvertToSeperateMzId("report.tdReport", "./out", FDR: 0.01,
                                   source: ReportSource.ProSightPD);
```

## Validating output

The output targets mzIdentML 1.1.0. To validate against the schema (e.g. with `xmllint`):

```bash
xmllint --noout --schema mzIdentML1.1.0.xsd path/to/output.mzid
```

The schema is available from the [HUPO‑PSI mzIdentML repository](https://github.com/HUPO-PSI/mzIdentML).

## License

See the repository for license details.
