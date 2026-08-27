using NRTDP.tdReportConverter;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// TDPortal and ProSight PD write the same v4.0 schema, so the producer is inferred from content.
/// These pin that inference down, and pin down that --source overrides it.
/// </summary>
public class ReportSourceDetectionTests
{
    [Fact]
    public void TDPortal_report_is_detected_as_TDPortal()
    {
        using var report = SyntheticTdReport.TDPortal(codesetVersion: "4.0.0.81");
        using var subject = new OpenTDReport_4(report.Path);

        Assert.False(subject.IsProSightPD);
        Assert.Equal("4.0.0.81", subject.SoftwareVersion);
    }

    [Fact]
    public void ProSightPD_report_is_detected_as_ProSightPD()
    {
        using var report = SyntheticTdReport.ProSightPD();
        using var subject = new OpenTDReport_4(report.Path);

        Assert.True(subject.IsProSightPD);
        // ProSight PD records no software version; the JSON metadata override supplies one if wanted.
        Assert.Null(subject.SoftwareVersion);
    }

    [Fact]
    public void Empty_ResultParameter_alone_does_not_make_a_report_ProSightPD()
    {
        // A TDPortal report whose search parameters happen to be empty still carries the codeset
        // key, and must not be misread as ProSight PD - that is why detection needs both signals.
        using var report = SyntheticTdReport.Create(
            dbMetadata: new[] { ("GenerateBatchedTargetPufDbHT", "4.0.0.81"), ("reporting_version", "1.4") },
            resultParameterRows: 0);
        using var subject = new OpenTDReport_4(report.Path);

        Assert.False(subject.IsProSightPD);
        Assert.Equal("4.0.0.81", subject.SoftwareVersion);
    }

    [Fact]
    public void Missing_codeset_key_alone_does_not_make_a_report_ProSightPD()
    {
        // The mirror case: no codeset key, but search parameters are populated. Detection abstains
        // from calling it ProSight PD rather than deciding on a single signal.
        using var report = SyntheticTdReport.Create(
            dbMetadata: new[] { ("reporting_version", "1.4") },
            resultParameterRows: 1);
        using var subject = new OpenTDReport_4(report.Path);

        Assert.False(subject.IsProSightPD);
        Assert.Null(subject.SoftwareVersion);
    }

    [Fact]
    public void Source_TDPortal_overrides_detection_of_a_ProSightPD_report()
    {
        using var report = SyntheticTdReport.ProSightPD();
        using var subject = new OpenTDReport_4(report.Path, ReportSource.TDPortal);

        Assert.False(subject.IsProSightPD);
        // The override changes provenance; it cannot invent a version that isn't in the report.
        Assert.Null(subject.SoftwareVersion);
    }

    [Fact]
    public void Source_ProSightPD_overrides_detection_of_a_TDPortal_report()
    {
        using var report = SyntheticTdReport.TDPortal();
        using var subject = new OpenTDReport_4(report.Path, ReportSource.ProSightPD);

        Assert.True(subject.IsProSightPD);
        Assert.Null(subject.SoftwareVersion);
    }

    [Fact]
    public void Auto_is_the_default_source()
    {
        using var report = SyntheticTdReport.ProSightPD();
        using var withDefault = new OpenTDReport_4(report.Path);
        using var withExplicitAuto = new OpenTDReport_4(report.Path, ReportSource.Auto);

        Assert.Equal(withExplicitAuto.IsProSightPD, withDefault.IsProSightPD);
        Assert.True(withDefault.IsProSightPD);
    }
}
