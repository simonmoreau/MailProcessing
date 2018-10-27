
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
using System.Text.RegularExpressions;
using HtmlAgilityPack;
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
                string body = await req.Content.ReadAsStringAsync();

                if (body == null)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject("Please pass an Authorisation Code and a redirect URI in the request body"), Encoding.UTF8, "application/json")
                    };
                }

                // load snippet
                HtmlDocument htmlSnippet = new HtmlDocument();
                htmlSnippet.LoadHtml(body);

                // extract hrefs
                List<string> hrefTags = new List<string>();
                List<DownloadLink> downloadLinks = new List<DownloadLink>();
                hrefTags = ExtractAllAHrefTags(htmlSnippet);

                if (hrefTags.Count == 0)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(downloadLinks),
    Encoding.UTF8,
    "application/json")
                    };
                }

                hrefTags = hrefTags.Distinct().ToList();

                foreach (string hrefTag in hrefTags)
                {
                    if (System.Uri.IsWellFormedUriString(hrefTag, UriKind.RelativeOrAbsolute) &&
                    hrefTag.StartsWith("http", true, System.Globalization.CultureInfo.CurrentCulture))
                    {
                        using (var client = new HttpClient())
                        {
                            var request = new HttpRequestMessage()
                            {
                                RequestUri = new Uri(hrefTag),
                                Method = HttpMethod.Get
                            };

                            HttpResponseMessage response = client.SendAsync(request).Result;
                            string url = response.RequestMessage.RequestUri.ToString();


                            if (url.Contains("wetransfer.com"))
                            {
                                downloadLinks.Add(GetDownloadLinkFromWeTransfer(url));
                            }
                            else if (url.Contains("drive.google.com"))
                            {
                                downloadLinks.Add(GetDownloadLinkFromGoogleDrive(url));
                            }
                            else if (url.Contains("dropbox.com"))
                            {
                                downloadLinks.Add(GetDownloadLinkFromDropbox(url));
                            }
                            else
                            {
                                //Console.WriteLine(hrefTag + " - " + url);
                            }
                        }
                    }
                }

                // Remove duplicates
                downloadLinks = downloadLinks.Distinct().ToList();

                string jsonContent = JsonConvert.SerializeObject(downloadLinks);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonContent, Encoding.UTF8, "application/json") };
            }
            catch (Exception ex)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(ex.Message), Encoding.UTF8, "application/json")
                };
            }
        }

        private static DownloadLink GetDownloadLinkFromWeTransfer(string url)
        {
            string fileId = url.Replace("https://wetransfer.com/downloads/", "").Split('/')[0];
            string security_hash = url.Replace("https://wetransfer.com/downloads/", "").Split('/')[1];

            string uri = $"https://wetransfer.com/api/v4/transfers/{fileId}/download";
            WeTransfertPostBody body = new WeTransfertPostBody(security_hash);

            using (var client = new HttpClient())
            {
                string jsonContent = JsonConvert.SerializeObject(body);
                HttpContent content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                // HttpResponseMessage response = client.PostAsync(uri,content).Result;

                // string test = response.RequestMessage.RequestUri.ToString();
                HttpResponseMessage response = client.PostAsync(uri, content).Result;

                string responseString = response.Content.ReadAsStringAsync().Result;

                try
                {
                    response.EnsureSuccessStatusCode();
                }
                catch
                {
                    throw new Exception(responseString);
                }

                WeTransfertLink weTransfertLink = (WeTransfertLink)JsonConvert.DeserializeObject(responseString, typeof(WeTransfertLink));

                Uri weTRansfertURI = new Uri(weTransfertLink.direct_link);
                string filename = System.IO.Path.GetFileName(weTRansfertURI.AbsolutePath);
                filename = System.Net.WebUtility.UrlDecode(System.Net.WebUtility.HtmlDecode(filename));

                return new DownloadLink(weTransfertLink.direct_link,filename);
            }
        }

        private static DownloadLink GetDownloadLinkFromGoogleDrive(string url)
        {

            // https://drive.google.com/file/d/1eQdiLwzSvAm5dZBr3SwwETD80ceJIscA/view

            // https://drive.google.com/uc?export=download&id=1eQdiLwzSvAm5dZBr3SwwETD80ceJIscA

            string fileId = url.Replace("https://drive.google.com/file/d/", "").Split('/')[0];

            string uri = $"https://drive.google.com/uc?export=download&id={fileId}";

            return new DownloadLink(uri);
        }

        private static DownloadLink GetDownloadLinkFromDropbox(string url)
        {

            // https://www.dropbox.com/s/puy4h8piv34sj6i/Getting%20Started.rtf?dl=0

            // https://www.dropbox.com/s/a1b2c3d4ef5gh6/example.docx?dl=1
            string uri = url;
            if (url.Contains("dl=0"))
            {
                uri = url.Replace("dl=0", "dl=1");
            }
            else
            {
                uri = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(url, "dl", "1");
            }

            return new DownloadLink(uri);
        }

        /// <summary>
        /// Extract all anchor tags using HtmlAgilityPack
        /// </summary>
        /// <param name="htmlSnippet"></param>
        /// <returns></returns>
        private static List<string> ExtractAllAHrefTags(HtmlDocument htmlSnippet)
        {
            List<string> hrefTags = new List<string>();

            foreach (HtmlNode link in htmlSnippet.DocumentNode.SelectNodes("//a[@href]"))
            {
                HtmlAttribute att = link.Attributes["href"];
                hrefTags.Add(att.Value);
            }

            return hrefTags;
        }


    }

    public class WeTransfertPostBody
    {
        public string security_hash;
        public string domain_user_id;

        public WeTransfertPostBody(string Security_hash)
        {
            security_hash = Security_hash;
            domain_user_id = "c2ffc9a5-9b95-4570-b270-f90483590452";
        }
    }

    public class WeTransfertLink
    {
        public string direct_link;
    }

    public class DownloadLink
    {
        public DownloadLink(string url)
        {
            link = url;
            name = GetFileName(url);
        }

        public DownloadLink(string url, string Name)
        {
            link = url;
            name = Name;
        }

        public string link;
        public string name;

        private string GetFileName(string url)
        {
            using (WebClient client = new WebClient())
            {
                client.OpenRead(url);

                string header_contentDisposition = client.ResponseHeaders["content-disposition"];
                string filename = new System.Net.Mime.ContentDisposition(header_contentDisposition).FileName;
                return filename;
            }
        }
    }
}
