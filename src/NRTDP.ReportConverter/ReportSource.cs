namespace NRTDP.tdReportConverter
{
    /// <summary>
    /// Which software produced a tdReport. TDPortal and ProSight PD write the same v4.0 schema,
    /// so the producer has to be inferred from content (see <see cref="OpenTDReport_4"/>); this
    /// lets a caller override that inference when the heuristic gets it wrong.
    /// </summary>
    public enum ReportSource
    {
        /// <summary>Infer the producer from the report's contents. The default.</summary>
        Auto = 0,

        /// <summary>Treat the report as TDPortal output, whatever detection would have said.</summary>
        TDPortal,

        /// <summary>Treat the report as ProSight PD output, whatever detection would have said.</summary>
        ProSightPD,
    }
}
