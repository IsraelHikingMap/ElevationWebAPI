using ElevationWebApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using NSubstitute;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace ElevationWebApiTests;

[TestClass]
public sealed class MemoryMapElevationProviderTests
{
    private MemoryMapElevationProvider provider;
    
    private string? GetSourceFileDirectory([CallerFilePath] string sourceFilePath = "")
    {
        return Path.GetDirectoryName(sourceFilePath);
    }

    
    [TestInitialize]
    public void Initialize()
    {
        var hosting = Substitute.For<IWebHostEnvironment>();
        hosting.ContentRootFileProvider = new PhysicalFileProvider( GetSourceFileDirectory()!);
        var logger = Substitute.For<ILogger<MemoryMapElevationProvider>>();
        provider = new MemoryMapElevationProvider(hosting, logger);
    }
    
    
    [TestMethod]
    public void CheckElevationAt1_1_ShouldReturnFromFile()
    {
        provider.Initialize().Wait();
        var elevation = provider.GetElevation([[1.0, 1.0]]).Result;
        Assert.AreEqual(elevation[0], 1291, 0.1);
    }
    
    [TestMethod]
    public void CheckElevationAt2_2_ShouldBeZero()
    {
        provider.Initialize().Wait();
        var elevation = provider.GetElevation([[2.0, 2.0]]).Result;
        Assert.AreEqual(elevation[0], 0);
    }
}