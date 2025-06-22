using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace ElevationWebApi
{
    internal static class ElevationHelper
    {
        public const string ELEVATION_CACHE = "elevation-cache";

        private static readonly Regex HGT_NAME =
            new(@"(?<latHem>N|S)(?<lat>\d{2})(?<lonHem>W|E)(?<lon>\d{3})(.*)\.hgt");
        
        public static bool ValidateFolder(IFileProvider fileProvider, ILogger logger)
        {
            if (fileProvider.GetDirectoryContents(ELEVATION_CACHE).Any() == false)
            {
                logger.LogError($"Elevation service initialization: The folder: {ELEVATION_CACHE} does not exists, please make sure this folder exists");
                return false;
            }

            var hgtFiles = fileProvider.GetDirectoryContents(ELEVATION_CACHE);
            if (hgtFiles.Any())
            {
                return true;
            }
            logger.LogError($"Elevation service initialization: There are no file in folder: {ELEVATION_CACHE}");
            return false;

        }

        public static Coordinate FileNameToKey(string fileName)
        {
            var match = HGT_NAME.Match(fileName);
            if (!match.Success)
            {
                return null;
            }

            var latHem = match.Groups["latHem"].Value == "N" ? 1 : -1;
            var bottomLeftLat = int.Parse(match.Groups["lat"].Value) * latHem;
            var lonHem = match.Groups["lonHem"].Value == "E" ? 1 : -1;
            var bottomLeftLng = int.Parse(match.Groups["lon"].Value) * lonHem;
            return new Coordinate(bottomLeftLng, bottomLeftLat);
        }
        
        public static void UnzipIfNeeded(IFileProvider fileProvider, ILogger logger)
        {
            var hgtFiles = fileProvider.GetDirectoryContents(ELEVATION_CACHE);
            foreach (var hgtFile in hgtFiles)
            {
                if (hgtFile.PhysicalPath.EndsWith(".bz2"))
                {
                    var hgtFilePath = hgtFile.PhysicalPath.Replace(".bz2", "");
                    if (hgtFiles.FirstOrDefault(f => f.PhysicalPath == hgtFilePath) != null)
                    {
                        continue;
                    }
                    logger.LogInformation($"Starting decompressing file {hgtFile.Name}");
                    BZip2.Decompress(hgtFile.CreateReadStream(),
                        File.Create(hgtFilePath), true);
                    logger.LogInformation($"Finished decompressing file {hgtFile.Name}");
                } 
                else if (hgtFile.PhysicalPath.EndsWith(".zip"))
                {
                    var hgtFilePath = hgtFile.PhysicalPath.Replace(".zip", "");
                    if (hgtFiles.FirstOrDefault(f => f.PhysicalPath == hgtFilePath) != null)
                    {
                        continue;
                    }
                    logger.LogInformation($"Starting decompressing file {hgtFile.Name}");
                    var fastZip = new FastZip();
                    var cacheFolder = fileProvider.GetFileInfo(ELEVATION_CACHE).PhysicalPath;
                    fastZip.ExtractZip(hgtFile.PhysicalPath, cacheFolder, null);
                    logger.LogInformation($"Finished decompressing file {hgtFile.Name}");
                } 
            }
        }

        public static int SamplesFromLength(long length)
        {
            return (int) (Math.Sqrt(length / 2.0) + 0.5);
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
        public static double BiLinearInterpolation(CoordinateZ p11, CoordinateZ p12, CoordinateZ p21, CoordinateZ p22,
            Coordinate p)
        {
            var fx = (p.X - p11.X) / (p21.X - p11.X);
            var fy = (p.Y - p11.Y) / (p12.Y / p11.Y);

            var r1 = p11.Z * (1 - fx) + p21.Z * fx;
            var r2 = p12.Z * (1 - fx) + p22.Z * fx;

            return r1 * (1 - fy) + r2 * fy;
        }

        /// <summary>
        /// Get the elevation of the two adjacent indices (i,j), (i, j+1)
        /// Then converts them to 3d points 
        /// </summary>
        /// <param name="i">i Index in file</param>
        /// <param name="j">j index in file</param>
        /// <param name="getBytes">A method to get the relevant bytes</param>
        /// <returns></returns>
        public static (CoordinateZ, CoordinateZ) GetElevationForLocation(int i, int j, Func<int, int, byte[]> getBytes)
        {
            var byteArray = getBytes(i, j);
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
    }
}