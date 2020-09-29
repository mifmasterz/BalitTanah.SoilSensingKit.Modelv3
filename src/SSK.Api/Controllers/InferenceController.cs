using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SSK.Api.Data;
using SSK.Api.Models;
using SSK.Core;
using SSK.Core.Models;

namespace SSK.Api.Controllers
{
    [ApiController]
    [Produces("application/json")]
    [Route("api/[controller]")]
    //[Authorize]
    public class InferenceController : ControllerBase
    {
        static Engine engine;
    
        public InferenceController()
        {
            if (engine == null)
            {
                engine = new Engine();
            }

        }
        /// <summary>
        /// Data input yang dimasukkan dari array 0 - akhir itu nilai reflectance dari wave length: 2501.982414 sampe 1350.724346
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        [HttpPost("[action]")]
        public async Task<IActionResult> ProcessData([FromBody] DataInput data)
        {


            var hasil = new OutputData() { IsSucceed = true, ErrorMessage=String.Empty };
            try
            {
                hasil.Data = engine.Predict(AppContants.ModelPath,data.Reflectance);

            }
            catch (Exception ex)
            {
                hasil.IsSucceed = false;
                hasil.ErrorMessage = ex.Message;
            }
            return Ok(hasil);
        }

    }
}