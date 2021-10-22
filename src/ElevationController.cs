using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ElevationMicroService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ElevationController : ControllerBase
    {
        private readonly ElevationProvider _elevationProvider;

        /// <summary>
        /// Controller's constructor
        /// </summary>
        /// <param name="elevationProvider"></param>
        public ElevationController(ElevationProvider elevationProvider)
        {
            _elevationProvider = elevationProvider;
        }

        /// <summary>
        /// Get elevation for the given points.
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
        
        [HttpPost]
        public Task<double[]> PostElevation([FromBody] double[][] points)
        {
            return _elevationProvider.GetElevation(points);
        }
    }
}