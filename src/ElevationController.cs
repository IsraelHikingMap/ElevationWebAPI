using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace ElevationWebApi
{
    /// <summary>
    /// The elevation controller and entry point
    /// </summary>
    [ApiController]
    [Route("")]
    public class ElevationController : ControllerBase
    {
        private readonly IElevationProvider _elevationProvider;

        /// <summary>
        /// Controller's constructor
        /// </summary>
        /// <param name="elevationProvider"></param>
        public ElevationController(IElevationProvider elevationProvider)
        {
            _elevationProvider = elevationProvider;
        }

        /// <summary>
        /// Get elevation for the given points.
        /// This call might be limited by the total address size, see POST for unlimited number
        /// </summary>
        /// <param name="points">The points array - each point should be latitude,longitude and use '|' to separate between points</param>
        /// <returns>An array of elevation values according to given points order</returns>
        [HttpGet]
        public Task<double[]> GetElevation(string points)
        {
            var pointsArray = points.Split('|').Select(p => new []
            {
                double.Parse(p.Split(",").First()),
                double.Parse(p.Split(",").Last())
            }).ToArray();
            return _elevationProvider.GetElevation(pointsArray);
        }
        
        /// <summary>
        /// Allows retrieving elevation for points
        /// </summary>
        /// <param name="points">An array of points array - each point is an array [lon, lat]</param>
        /// <returns>An array of elevation correlating to the given points array order</returns>
        [HttpPost]
        public Task<double[]> PostElevation([FromBody] double[][] points)
        {
            return _elevationProvider.GetElevation(points);
        }
    }
}