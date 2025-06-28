using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace ElevationWebApi
{
    internal record FileAndSamples(MemoryMappedFile File, int Samples);

    /// <summary>
    /// The elevation provider based on memory mapped hgt files
    /// </summary>
    public class MemoryMapElevationProvider : IElevationProvider
    {
        private readonly ILogger<MemoryMapElevationProvider> _logger;
        private readonly IFileProvider _fileProvider;
        private readonly ConcurrentDictionary<Coordinate, Task<FileAndSamples>> _mappedFilesCache;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="webHostEnvironment"></param>
        /// <param name="logger"></param>
        public MemoryMapElevationProvider(IWebHostEnvironment webHostEnvironment, ILogger<MemoryMapElevationProvider> logger)
        {
            _logger = logger;
            _fileProvider = webHostEnvironment.ContentRootFileProvider;
            _mappedFilesCache = new();
        }

        /// <summary>
        /// Initializes the provider by reading the elevation-cache directory,
        /// extracting the zip/bz2 files if needed and memory mapping them to a dictionary
        /// </summary>
        public Task Initialize()
        {
            _logger.LogInformation("Initialization of Memory Map Elevation Provider.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Calculates the elevation of a point using a preloaded data, using bilinear interpolation
        /// </summary>
        /// <param name="latLngs">An array of point to calculate elevation for</param>
        /// <returns>A task with the elevation results</returns>
        public Task<double[]> GetElevation(double[][] latLngs)
        {
            var tasks = latLngs.Select(async latLng =>
            {
                var key = new Coordinate(Math.Floor(latLng[0]), Math.Floor(latLng[1]));
                if (!_mappedFilesCache.ContainsKey(key))
                {
                    var filePath = Path.Join(ElevationHelper.ELEVATION_CACHE, ElevationHelper.KeyToFileName(key));
                    var fileInfo = _fileProvider.GetFileInfo(filePath);
                    if (!fileInfo.Exists)
                    {
                        return 0;
                    }
                    _logger.LogInformation($"Loading {fileInfo.PhysicalPath} into memory mapped cache");
                    _mappedFilesCache[key] = Task.Run(() => new FileAndSamples(MemoryMappedFile.CreateFromFile(fileInfo.PhysicalPath!, FileMode.Open), ElevationHelper.SamplesFromLength(fileInfo.Length)));
                }
                var info = await _mappedFilesCache[key];

                var exactLocation = new Coordinate(Math.Abs(latLng[0] - key.X) * (info.Samples - 1),
                    (1 - Math.Abs(latLng[1] - key.Y)) * (info.Samples - 1));

                var i = (int) exactLocation.Y;
                var j = (int) exactLocation.X;
                if (i == info.Samples - 1) i--;
                if (j == info.Samples - 1) j--;

                var (p11, p21) = GetElevationForLocation(i, j, info);
                var (p12, p22) = GetElevationForLocation(i + 1, j, info);
                return ElevationHelper.BiLinearInterpolation(p11, p12, p21, p22, exactLocation);
            }).ToArray();
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Get the elevation of the two adjacent indices (i,j), (i, j+1)
        /// Then converts them to 3d points 
        /// </summary>
        /// <param name="i">I Index in file</param>
        /// <param name="j">J index in file</param>
        /// <param name="info">The info relevant to the file</param>
        /// <returns></returns>
        private (CoordinateZ, CoordinateZ) GetElevationForLocation(int i, int j, FileAndSamples info)
        {
            return ElevationHelper.GetElevationForLocation(i, j, (i1, j1) =>
            {
                var byteIndex = (i1 * info.Samples + j1) * 2;
                using var stream = info.File.CreateViewStream(byteIndex, 4);
                byte[] byteArray = new byte[4];
                stream.ReadExactly(byteArray);
                return byteArray;
            });
        }
    }
}