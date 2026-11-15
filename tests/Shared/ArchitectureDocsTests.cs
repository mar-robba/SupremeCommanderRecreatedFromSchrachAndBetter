using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Rts.ArchitectureDocs;

/// <summary>
/// The architecture docs must match the code (docs/ARCHITECTURE.md "Keeping these docs true"). Runs
/// <c>python3 -I tools/arch/arch.py check</c> on the checkout this test was built from. This file is compiled into
/// every test project (a link in each csproj), so whichever suite an agent runs, the check runs.
/// </summary>
public class ArchitectureDocsTests
{
    [Fact]
    public void ArchitectureDocsMatchTheCode()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Rts.slnx"))) dir = dir.Parent;
        Assert.True(dir != null, "repository root (Rts.slnx) not found above " + AppContext.BaseDirectory);

        var psi = new ProcessStartInfo("python3")
        {
            WorkingDirectory = dir!.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(Path.Combine("tools", "arch", "arch.py"));
        psi.ArgumentList.Add("check");

        Process p;
        try { p = Process.Start(psi)!; }
        catch (Win32Exception e)
        {
            Assert.Fail("python3 is needed to check the architecture docs (tools/arch/arch.py): " + e.Message);
            return;
        }
        using (p)
        {
            var err = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, "\n" + output + err.Result);
        }
    }
}
