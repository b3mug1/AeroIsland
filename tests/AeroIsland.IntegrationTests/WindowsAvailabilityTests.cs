using AeroIsland.Windows;
using Xunit;

namespace AeroIsland.IntegrationTests;

public sealed class WindowsAvailabilityTests
{
    [Fact]
    public void Audio_level_read_tolerates_a_missing_endpoint()
    {
        var state = AudioLevelReader.Read();
        if (state is { } audio)
        {
            Assert.True(float.IsFinite(audio.Level));
            Assert.InRange(audio.Level, 0f, 1f);
        }
    }

    [Fact]
    public async Task Media_discovery_does_not_require_a_player()
    {
        using var service = new MediaService();
        await service.StartAsync();
        Assert.NotNull(service.SessionIds);
    }
}
