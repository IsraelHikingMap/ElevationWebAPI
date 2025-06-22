using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace ElevationWebApi
{
    internal record BytesAndSamples(byte[] Bytes, int Samples);
    
    /// <inheritdoc/>
    public class InMemoryElevationProvider : IElevationProvider
    {
        private readonly ILogger<InMemoryElevationProvider> _logger;
        private readonly IFileProvider _fileProvider;
        private readonly ConcurrentDictionary<Coordinate, Task<BytesAndSamples>> _initializationTaskPerLatLng;
        
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="webHostEnvironment"></param>
        /// <param name="logger"></param>
        public InMemoryElevationProvider(IWebHostEnvironment webHostEnvironment, ILogger<InMemoryElevationProvider> logger)
        {
            _logger = logger;
            _fileProvider = webHostEnvironment.ContentRootFileProvider;
            _initializationTaskPerLatLng = new();
        }

        /// <inheritdoc/>
        public async Task Initialize()
        {
            if (!ElevationHelper.ValidateFolder(_fileProvider, _logger))
            {
                return;
            }

            ElevationHelper.UnzipIfNeeded(_fileProvider, _logger);
            var hgtFiles = _fileProvider.GetDirectoryContents(ElevationHelper.ELEVATION_CACHE);

            foreach (var hgtFile in hgtFiles)
            {
                if (!hgtFile.PhysicalPath.EndsWith(".hgt"))
                {
                    continue;
                }
                var key = ElevationHelper.FileNameToKey(hgtFile.Name);
                _initializationTaskPerLatLng[key] = Task.Run(() =>
                {
                    var stream = hgtFile.CreateReadStream();
                    using var memoryStream = new MemoryStream();
                    StreamUtils.Copy(stream, memoryStream, new byte[4096]);
                    var bytes = memoryStream.ToArray();
                    return new BytesAndSamples(bytes, ElevationHelper.SamplesFromLength(bytes.Length));
                });
            }
            
            await Task.WhenAll(_initializationTaskPerLatLng.Values);
        }

        /// <inheritdoc/>
        public async Task<double[]> GetElevation(double[][] latLngs)
        {
            var elevation = new List<double>();
            foreach (var latLng in latLngs)
            {
                var key = new Coordinate(Math.Floor(latLng[0]), Math.Floor(latLng[1]));
                if (_initializationTaskPerLatLng.ContainsKey(key) == false)
                {
                    elevation.Add(0);
                    continue;
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
                elevation.Add(ElevationHelper.BiLinearInterpolation(p11, p12, p21, p22, exactLocation));
            }

            return elevation.ToArray();
        }

        private (CoordinateZ, CoordinateZ) GetElevationForLocation(int i, int j, BytesAndSamples info)
        {
            return ElevationHelper.GetElevationForLocation(i, j, (i1, j1) =>
            {
                var byteIndex = (i1 * info.Samples + j1) * 2;
                return new []
                {
                    info.Bytes[byteIndex], info.Bytes[byteIndex + 1], info.Bytes[byteIndex + 2],
                    info.Bytes[byteIndex + 3]
                };
            });
        }
    }
}