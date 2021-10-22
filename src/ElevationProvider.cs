using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate.QuadEdge;

namespace ElevationMicroService
{
    internal record FileAndSize(MemoryMappedFile File, long Length);

    public class ElevationProvider
    {
        private const string ELEVATION_CACHE = "elevation-cache";
        private static readonly Regex HGT_NAME = new Regex(@"(?<latHem>N|S)(?<lat>\d{2})(?<lonHem>W|E)(?<lon>\d{3})(.*)\.hgt");
        private readonly ILogger _logger;
        private readonly IFileProvider _fileProvider;
        private readonly ConcurrentDictionary<Coordinate, Task<FileAndSize>> _initializationTaskPerLatLng;

        public ElevationProvider(IWebHostEnvironment webHostEnvironment)
        {
            //_logger = logger;
            _fileProvider = webHostEnvironment.ContentRootFileProvider;
            _initializationTaskPerLatLng = new();
        }

        public async Task Initialize()
        {
            if (_fileProvider.GetDirectoryContents(ELEVATION_CACHE).Any() == false)
            {
                //_logger.LogError($"Elevation service initialization: The folder: {ELEVATION_CACHE} does not exists, please make sure this folder exists");
                return;
            }
            var hgtFiles = _fileProvider.GetDirectoryContents(ELEVATION_CACHE);
            if (!hgtFiles.Any())
            {
                //_logger.LogError($"Elevation service initialization: There are no file in folder: {ELEVATION_CACHE}");
                return;
            }
            foreach (var hgtFile in hgtFiles)
            {
                var match = HGT_NAME.Match(hgtFile.Name);
                if (!match.Success)
                {
                    continue;
                }

                var latHem = match.Groups["latHem"].Value == "N" ? 1 : -1;
                var bottomLeftLat = int.Parse(match.Groups["lat"].Value) * latHem;
                var lonHem = match.Groups["lonHem"].Value == "E" ? 1 : -1;
                var bottomLeftLng = int.Parse(match.Groups["lon"].Value) * lonHem;
                var key = new Coordinate(bottomLeftLng, bottomLeftLat);
                _initializationTaskPerLatLng[key] = Task.Run(() =>
                {
                    if (hgtFile.PhysicalPath.EndsWith("hgt"))
                    {
                        return new FileAndSize(MemoryMappedFile.CreateFromFile(hgtFile.PhysicalPath, FileMode.Open), hgtFile.Length);
                    }

                    var fastZip = new FastZip();
                    var cacheFolder = _fileProvider.GetFileInfo(ELEVATION_CACHE).PhysicalPath;
                    fastZip.ExtractZip(hgtFile.PhysicalPath, cacheFolder, null);
                    File.Delete(hgtFile.PhysicalPath);
                    var hgtFilePath = hgtFile.PhysicalPath.Replace(".zip", "").Replace(".bz2", "");
                    var fileInfo = _fileProvider.GetFileInfo(Path.Join(ELEVATION_CACHE, Path.GetFileName(hgtFilePath)));

                    return new FileAndSize(MemoryMappedFile.CreateFromFile(fileInfo.PhysicalPath, FileMode.Open), fileInfo.Length);
                });
            }

            await Task.WhenAll(_initializationTaskPerLatLng.Values);
            //_logger.LogInformation($"Finished initializing elevation service, Found {hgtZipFiles.Count()} files.");
        }

        /// <summary>
        /// Calculates the elevation of a point using a preloaded data, using intepolation of 3 points in a plane:
        /// 3
        /// |    p
        /// |  
        /// 1______2
        /// </summary>
        /// <param name="latLngs">An array of point to calculate elevation for</param>
        /// <returns>A task with the elevation results</returns>
        public Task<double[]> GetElevation(double[][] latLngs)
        {
            var tasks = latLngs.Select(async latLng => {
                var key = new Coordinate(Math.Floor(latLng[0]), Math.Floor(latLng[1]));
                if (_initializationTaskPerLatLng.ContainsKey(key) == false)
                {
                    return 0;
                }

                var info = await _initializationTaskPerLatLng[key];
                
                int samples = (short) (Math.Sqrt(info.Length / 2.0) + 0.5);

                var exactLocation = new Coordinate(Math.Abs(latLng[0] - key.X) * (samples - 1), 
                    (1 - Math.Abs(latLng[1] - key.Y)) * (samples - 2));
                
                var i = (int) exactLocation.Y;
                var j = (int) exactLocation.X;

                if (i >= samples - 1 || j >= samples - 1)
                {
                    return GetElevationForLocation(i, j, samples, info.File);
                }

                var coordinate1 = new CoordinateZ(j, i, GetElevationForLocation(i, j, samples, info.File));
                var coordinate2 = new CoordinateZ(j + 1, i, GetElevationForLocation(i, j + 1, samples, info.File));
                var coordinate3 = new CoordinateZ(j, i + 1, GetElevationForLocation(i + 1, j, samples, info.File));
                
                return Vertex.InterpolateZ(exactLocation, coordinate1, coordinate2, coordinate3);
            }).ToArray();
            return Task.WhenAll(tasks);
        }
        
        private short GetElevationForLocation(int i, int j, int samples, MemoryMappedFile file)
        {
            var byteIndex = (i * samples + j) * 2;
            var stream = file.CreateViewStream(byteIndex, 2);
            Span<byte> byteArray = new byte[2];
            stream.Read(byteArray);
            short currentElevation = BitConverter.ToInt16(new[] { byteArray[1], byteArray[0] }, 0);
            // if hgt file contains -32768, use 0 instead
            if (currentElevation == short.MinValue)
            {
                currentElevation = 0;
            }

            return currentElevation;
        }
    }
    
    /*
     * var i = (int) (samples - 1 - Math.Abs(latLng[1] - key.Y) * samples);
                var j = (int) (Math.Abs(latLng[0] - key.X) * samples);

                if ((i >= samples - 1) || (j >= samples - 1))
                {
                    return GetElevationForLocation(i, j, samples, info.File);
                }

                var coordinate1 = new CoordinateZ(i, j, GetElevationForLocation(i, j, samples, info.File));
                var coordinate2 = new CoordinateZ(i + 1, j, GetElevationForLocation(i + 1, j, samples, info.File));
                var coordinate3 = new CoordinateZ(i, j + 1, GetElevationForLocation(i, j + 1, samples, info.File));
                var exactLocation = new Coordinate(samples - 1 - Math.Abs(latLng[1] - key.Y) * samples,
                    Math.Abs(latLng[0] - key.X) * samples);
                return Vertex.InterpolateZ(exactLocation, coordinate1, coordinate2, coordinate3);
     */
    
}