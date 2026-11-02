using Methadrenaline.Server.Voting;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class ModeVoteTokenFileTests
{
    [Fact]
    public void UnixNativeReaderAcceptsOwnerOnlyFileAndRejectsUnsafePermissionsAndLinks()
    {
        if (!OperatingSystem.IsLinux()) return;
        string file = Path.Combine(Path.GetTempPath(), "mode-vote-token-test-" + Guid.NewGuid().ToString("N"));
        string link = file + "-link";
        try
        {
            File.WriteAllText(file, new string('x', 32) + "\n");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Equal(new string('x', 32), ModeVoteTokenFile.Load(file));
            File.SetUnixFileMode(file, UnixFileMode.UserRead);
            Assert.Equal(new string('x', 32), ModeVoteTokenFile.Load(file));
            File.CreateSymbolicLink(link, file);
            Assert.Throws<IOException>(() => ModeVoteTokenFile.Load(link));
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            Assert.Throws<UnauthorizedAccessException>(() => ModeVoteTokenFile.Load(file));
        }
        finally { File.Delete(link); File.Delete(file); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(257)]
    [InlineData(513)]
    [InlineData(4096)]
    public void UnixNativeReaderRejectsInvalidLengthsAndReleasesDescriptor(int length)
    {
        if (!OperatingSystem.IsLinux()) return;
        string file = Path.Combine(Path.GetTempPath(), "mode-vote-token-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(file, new string('x', length));
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Throws<InvalidOperationException>(() => ModeVoteTokenFile.Load(file));
            File.WriteAllText(file, new string('y', 256));
            Assert.Equal(new string('y', 256), ModeVoteTokenFile.Load(file));
        }
        finally { File.Delete(file); }
    }
}
