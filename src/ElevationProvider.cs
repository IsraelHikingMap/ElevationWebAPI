using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Zip;
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
    public class ElevationProvider
    {
        private const string ELEVATION_CACHE = "elevation-cache";

        private static readonly Regex HGT_NAME =
            new(@"(?<latHem>N|S)(?<lat>\d{2})(?<lonHem>W|E)(?<lon>\d{3})(.*)\.hgt");

        private readonly ILogger<ElevationProvider> _logger;
        private readonly IFileProvider _fileProvider;
        private readonly ConcurrentDictionary<Coordinate, Task<FileAndSamples>> _initializationTaskPerLatLng;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="webHostEnvironment"></param>
        /// <param name="logger"></param>
        public ElevationProvider(IWebHostEnvironment webHostEnvironment, ILogger<ElevationProvider> logger)
        {
            _logger = logger;
            _fileProvider = webHostEnvironment.ContentRootFileProvider;
            _initializationTaskPerLatLng = new();
        }

        /// <summary>
        /// Initializes the provider by reading the elevation-cache directory,
        /// extracting the zip/bz2 files if needed and memory mapping them to a dictionary
        /// </summary>
        public async Task Initialize()
        {
            if (_fileProvider.GetDirectoryContents(ELEVATION_CACHE).Any() == false)
            {
                _logger.LogError($"Elevation service initialization: The folder: {ELEVATION_CACHE} does not exists, please make sure this folder exists");
                return;
            }

            var hgtFiles = _fileProvider.GetDirectoryContents(ELEVATION_CACHE);
            if (!hgtFiles.Any())
            {
                _logger.LogError($"Elevation service initialization: There are no file in folder: {ELEVATION_CACHE}");
                return;
            }

            var duplicateFilesPath = hgtFiles.Select(f => f.PhysicalPath.Replace(".zip", "").Replace(".bz2", ""))
                .GroupBy(f => f)
                .Where(f => f.Count() == 2)
                .Select(g => g.First())
                .ToArray();
            foreach (var fileName in duplicateFilesPath)
            {
                if (File.Exists(fileName + ".zip"))
                {
                    _logger.LogInformation($"Deleting duplicate file {fileName}");
                    File.Delete(fileName + ".zip");
                }
                if (File.Exists(fileName + ".bz2"))
                {
                    _logger.LogInformation($"Deleting duplicate file {fileName}");
                    File.Delete(fileName + ".bz2");
                }
            }
            
            hgtFiles = _fileProvider.GetDirectoryContents(ELEVATION_CACHE);
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
                    IFileInfo fileInfo = hgtFile;
                    if (hgtFile.PhysicalPath.EndsWith(".bz2"))
                    {
                        _logger.LogInformation($"Starting decompressing file {hgtFile.Name}");
                        var hgtFilePath = hgtFile.PhysicalPath.Replace(".bz2", "");
                        BZip2.Decompress(hgtFile.CreateReadStream(),
                            File.Create(hgtFilePath), true);
                        File.Delete(hgtFile.PhysicalPath);
                        fileInfo = _fileProvider.GetFileInfo(Path.Join(ELEVATION_CACHE, Path.GetFileName(hgtFilePath)));
                        _logger.LogInformation($"Finished decompressing file {hgtFile.Name}");
                    } 
                    else if (hgtFile.PhysicalPath.EndsWith(".zip"))
                    {
                        _logger.LogInformation($"Starting decompressing file {hgtFile.Name}");
                        var fastZip = new FastZip();
                        var cacheFolder = _fileProvider.GetFileInfo(ELEVATION_CACHE).PhysicalPath;
                        fastZip.ExtractZip(hgtFile.PhysicalPath, cacheFolder, null);
                        File.Delete(hgtFile.PhysicalPath);
                        var hgtFilePath = hgtFile.PhysicalPath.Replace(".zip", "");
                        fileInfo = _fileProvider.GetFileInfo(Path.Join(ELEVATION_CACHE, Path.GetFileName(hgtFilePath)));
                        _logger.LogInformation($"Finished decompressing file {hgtFile.Name}");
                    } 
                    else if (hgtFile.PhysicalPath.EndsWith("hgt"))
                    {
                        fileInfo = hgtFile;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"Files in {ELEVATION_CACHE} folder should be either hgt, zip or bz2 but found {Path.GetExtension(fileInfo.PhysicalPath)}");
                    }
                    int samples = (int) (Math.Sqrt(fileInfo.Length / 2.0) + 0.5);
                    return new FileAndSamples(MemoryMappedFile.CreateFromFile(fileInfo.PhysicalPath, FileMode.Open), samples);
                });
            }

            await Task.WhenAll(_initializationTaskPerLatLng.Values);
            _logger.LogInformation($"Finished initializing elevation service, Found {hgtFiles.Count()} files.");
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
                if (_initializationTaskPerLatLng.ContainsKey(key) == false)
                {
                    return 0;
                }

                var info = await _initializationTaskPerLatLng[key];

                var exactLocation = new Coordinate(Math.Abs(latLng[0] - key.X) * (info.Samples - 1),
                    (1 - Math.Abs(latLng[1] - key.Y)) * (info.Samples - 1));

                var i = (int) exactLocation.Y;
                var j = (int) exactLocation.X;
                if (i == info.Samples - 1) i--;
                if (j == info.Samples - 1) j--;

                var (p11, p21) = GetElevationForLocation(i, j, info);
                var (p12, p22) = GetElevationForLocation(i + 1, j, info);
                return BiLinearInterpolation(p11, p12, p21, p22, exactLocation);
            }).ToArray();
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Get the elevation of the two adjacent indices (i,j), (i, j+1)
        /// Then converts them to 3d points 
        /// </summary>
        /// <param name="i">i Index in file</param>
        /// <param name="j">j index in file</param>
        /// <param name="info">The info relevant to the file</param>
        /// <returns></returns>
        private (CoordinateZ, CoordinateZ) GetElevationForLocation(int i, int j, FileAndSamples info)
        {
            var byteIndex = (i * info.Samples + j) * 2;
            using var stream = info.File.CreateViewStream(byteIndex, 4);
            Span<byte> byteArray = new byte[4];
            stream.Read(byteArray);
            short elevationFirst = BitConverter.ToInt16(new[] {byteArray[1], byteArray[0]}, 0);
            short elevationSecond = BitConverter.ToInt16(new[] {byteArray[3], byteArray[2]}, 0);
            // if hgt file contains -32768, use 0 instead
            if (elevationFirst == short.MinValue)
            {
                elevationFirst = 0;
            }

            if (elevationSecond == short.MinValue)
            {
                elevationSecond = 0;
            }

            return (new CoordinateZ(j, i, elevationFirst), new CoordinateZ(j + 1, i, elevationSecond));
        }

        /// <summary>
        /// Returns the bilinear interpolation according to the following four points:
        /// p12 --- p22
        ///  |    p |
        ///  |      |
        /// p11----p21
        /// </summary>
        /// <param name="p11">Lower left corner</param>
        /// <param name="p12">Upper left corner</param>
        /// <param name="p21">Lower right corner</param>
        /// <param name="p22">Upper right corner</param>
        /// <param name="p">The point to calculate</param>
        /// <returns>The bilinear interpolation value for p</returns>
        private double BiLinearInterpolation(CoordinateZ p11, CoordinateZ p12, CoordinateZ p21, CoordinateZ p22,
            Coordinate p)
        {
            var fx = (p.X - p11.X) / (p21.X - p11.X);
            var fy = (p.Y - p11.Y) / (p12.Y / p11.Y);

            var r1 = p11.Z * (1 - fx) + p21.Z * fx;
            var r2 = p12.Z * (1 - fx) + p22.Z * fx;

            return r1 * (1 - fy) + r2 * fy;
        }
    }
}