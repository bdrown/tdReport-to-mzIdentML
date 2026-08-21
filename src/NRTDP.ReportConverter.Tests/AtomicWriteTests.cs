using System.Text;
using NRTDP.tdReportConverter;

namespace NRTDP.ReportConverter.Tests;

/// <summary>
/// --skip-existing decides what to redo purely from which files are present, so "present" has to
/// mean "complete". A run interrupted mid-write must not leave behind a half-written .mzid that a
/// later resume would accept as finished - which is exactly what a reboot for a system update
/// produced during a real 120-file conversion.
/// </summary>
public class AtomicWriteTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("atomic-write-").FullName;

    private string PathFor(string name) => Path.Combine(_directory, name);

    private static void WriteText(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    [Fact]
    public void Completed_write_lands_at_the_target_path()
    {
        var target = PathFor("done.mzid");

        MzidmlWriter.WriteAtomically(target, stream => WriteText(stream, "<MzIdentML/>"));

        Assert.Equal("<MzIdentML/>", File.ReadAllText(target));
    }

    [Fact]
    public void Completed_write_leaves_no_partial_behind()
    {
        var target = PathFor("done.mzid");

        MzidmlWriter.WriteAtomically(target, stream => WriteText(stream, "<MzIdentML/>"));

        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public void Failed_write_creates_no_file_at_the_target_path()
    {
        // The whole point: a file that is present must be trustworthy, so a run that dies partway
        // must leave nothing for --skip-existing to find.
        var target = PathFor("interrupted.mzid");

        Assert.Throws<IOException>(() => MzidmlWriter.WriteAtomically(target, stream =>
        {
            WriteText(stream, "<MzIdentML>truncated...");
            throw new IOException("simulated interruption");
        }));

        Assert.False(File.Exists(target));
    }

    [Fact]
    public void Failed_write_cleans_up_its_partial()
    {
        var target = PathFor("interrupted.mzid");

        Assert.Throws<IOException>(() => MzidmlWriter.WriteAtomically(target, _ =>
            throw new IOException("simulated interruption")));

        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public void Failed_write_leaves_an_earlier_good_file_untouched()
    {
        // Re-converting a file that already exists must not destroy the good copy if it fails.
        var target = PathFor("existing.mzid");
        File.WriteAllText(target, "<MzIdentML>previous</MzIdentML>");

        Assert.Throws<IOException>(() => MzidmlWriter.WriteAtomically(target, stream =>
        {
            WriteText(stream, "<MzIdentML>replacement...");
            throw new IOException("simulated interruption");
        }));

        Assert.Equal("<MzIdentML>previous</MzIdentML>", File.ReadAllText(target));
    }

    [Fact]
    public void Successful_write_replaces_an_existing_file()
    {
        var target = PathFor("existing.mzid");
        File.WriteAllText(target, "<MzIdentML>previous</MzIdentML>");

        MzidmlWriter.WriteAtomically(target, stream => WriteText(stream, "<MzIdentML>replacement</MzIdentML>"));

        Assert.Equal("<MzIdentML>replacement</MzIdentML>", File.ReadAllText(target));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
