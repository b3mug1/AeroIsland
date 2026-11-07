using AeroIsland.Windows;
using Xunit;

namespace AeroIsland.IntegrationTests;

public sealed class WindowsAvailabilityTests
{
    [Fact]
    public async Task Media_discovery_does_not_require_a_player()
    {
        using var service = new MediaService();
        await service.StartAsync();
        Assert.NotNull(service.SessionIds);
    }
}
