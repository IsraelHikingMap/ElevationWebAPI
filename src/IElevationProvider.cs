using System.Threading.Tasks;

namespace ElevationWebApi
{
    /// <summary>
    /// Elevation provider
    /// </summary>
    public interface IElevationProvider
    {
        /// <summary>
        /// Initializes the provider by reading the elevation-cache directory,
        /// extracting the zip/bz2 files if needed and memory mapping them to a dictionary
        /// </summary>
        Task Initialize();

        /// <summary>
        /// Calculates the elevation of a point using a preloaded data, using bilinear interpolation
        /// </summary>
        /// <param name="latLngs">An array of point to calculate elevation for</param>
        /// <returns>A task with the elevation results</returns>
        Task<double[]> GetElevation(double[][] latLngs);
    }
}