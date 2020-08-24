

using Identity.Api.Models;
using Identity.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Identity.Api.Controllers
{
    [Route("api/[controller]")]
    public class UsersController : Controller
    {
        private readonly IIdentityRepository _identityRespository;
        private readonly ILogger<UsersController> _logger;

        public UsersController(IIdentityRepository identityRespository, ILogger<UsersController> logger)
        {
            _identityRespository = identityRespository;
            _logger = logger;
        }

        // GET api/users/5
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                _logger.LogInformation($"Getting user for Id : {id}.");
                var user = await _identityRespository.GetUserAsync(id);
                if (user != null)
                {
                    _logger.LogInformation($"Found user : {user.Name}.");
                    return Ok(user);
                }
                else
                {
                    _logger.LogInformation($"User not found for Id : {id}.");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "It broke :(");
                _logger.LogInformation($"Error getting user for Id : {id}. Exception : {e.ToString()}");
            }

            return Ok("User not found.");
        }

        // GET api/users/applicationcount/5
        [HttpGet("applicationcount/{id}")]
        public async Task<IActionResult> GetUserApplicantCount(string id)
        {
            var count = await _identityRespository.GetUserApplicationCountAsync(id);
            return Ok(count);
        }


        // POST api/users
        [HttpPost]
        public async Task<IActionResult> Post([FromBody]User value)
        {
            var user = await _identityRespository.UpdateUserAsync(value);
            return Ok(user);
        }
    }
}
