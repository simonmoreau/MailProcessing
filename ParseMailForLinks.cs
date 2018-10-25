
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
namespace MailParser
{
    public static class ParseMailForLinks
    {
        [FunctionName("ParseMailForLinks")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestMessage req,
            ILogger log)
        {
            try
            {
                log.LogInformation("Creating a new user");

                // Get request body
                string body = await req.Content.ReadAsAsync<string>();

                if (body == null)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject("Please pass an Authorisation Code and a redirect URI in the request body"), Encoding.UTF8, "application/json")
                    };
                }


                string jsonContent = JsonConvert.SerializeObject(body);

                return body == null
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("The user has not been created", Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonContent, Encoding.UTF8, "application/json") };
            }
            catch (Exception ex)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(ex.Message, Encoding.UTF8, "application/json") };
            }
        }
    }
}
