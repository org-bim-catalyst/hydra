using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NuGet.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using Newtonsoft.Json.Serialization;
using System.Text;
using System.Text.Json.Nodes;
using System.Drawing;
using Microsoft.Identity.Client;
using System.Security.Policy;
using System.Globalization;
using static System.Net.Mime.MediaTypeNames;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Google.Apis.PeopleService.v1.Data;
using Microsoft.AspNetCore.Mvc.Rendering;
using SendGrid.Helpers.Mail;

namespace AskLucy.Controllers
{
    [AllowAnonymous]
    public class ChatGPTController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private string _appKey = string.Empty;

        public ChatGPTController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _appKey = _config.GetValue<string>("ChatGPT")!;
        }

        [HttpPost("openai/chat")]
        public async Task<IActionResult> Chat(string model, string messages)
        {
            try
            {
                using (HttpClient client = _httpClientFactory.CreateClient("Default"))
                {

                    client.BaseAddress = new Uri(uriString: "https://api.openai.com");

                    client.DefaultRequestHeaders.Add(name: "authorization", value: $"Bearer {_appKey}");
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));//ACCEPT header


                    string data = @$"{{""model"": ""{model}"",""messages"": {messages}}}";

                    HttpContent httpContent = new StringContent(data, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = client.PostAsync($"/v1/chat/completions", httpContent).Result;
                    string result = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync())!.choices[0].message.content;
                    return Json(result);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(((int)HttpStatusCode.InternalServerError), ex.Message);
            }
        }

        [HttpPost("openai/draw")]
        public async Task<IActionResult> Draw(string prompt, string size, string n)
        {
            try
            {
                using (HttpClient client = _httpClientFactory.CreateClient("Default"))
                {

                    client.BaseAddress = new Uri(uriString: "https://api.openai.com");

                    client.DefaultRequestHeaders.Add(name: "authorization", value: $"Bearer {_appKey}");
                    
                    string data = @$"{{""prompt"": ""{prompt}"",""n"": {n}, ""size"":""{size}""}}";

                    HttpContent httpContent = new StringContent(data, Encoding.UTF8, "application/json");
                    httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    HttpResponseMessage response = client.PostAsync("/v1/images/generations", httpContent).Result;
                    dynamic result = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync())!;
                    
                    if(result.error != null)
                    {
                        string returnValue = result.error.message;
                        return new ContentResult()
                        {
                            StatusCode = (int)HttpStatusCode.BadRequest,
                            Content = returnValue,
                            ContentType = "text/plain"
                        };
                    }
                    else
                    {
                        string returnValue = result.data[0].url;
                        return Json(returnValue);
                    }

                }
            }
            catch (Exception ex)
            {
                return StatusCode(((int)HttpStatusCode.InternalServerError), ex.Message);
            }
        }

        [HttpPost("openai/translate")]
        public async Task<IActionResult> Translate(string model, string messages)
        {
            try
            {
                using (HttpClient client = _httpClientFactory.CreateClient("Default"))
                {

                    client.BaseAddress = new Uri(uriString: "https://api.openai.com");

                    client.DefaultRequestHeaders.Add(name: "authorization", value: $"Bearer {_appKey}");
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));//ACCEPT header


                    string data = @$"{{""model"": ""{model}"",""messages"": {messages}}}";

                    HttpContent httpContent = new StringContent(data, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = client.PostAsync("/v1/chat/completions", httpContent).Result;
                    string result = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync())!.choices[0].message.content;
                    return Json(result);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(((int)HttpStatusCode.InternalServerError), ex.Message);
            }
        }

        [HttpPost("openai/transcript")]
        public async Task<IActionResult> Transcript(string model, IFormFile file)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    file.CopyTo(ms);
                    byte[] bytes = ms.ToArray();

                    using (HttpClient client = _httpClientFactory.CreateClient("Default"))
                    {
                        using (MultipartFormDataContent httpContent = new MultipartFormDataContent())
                        {
                            httpContent.Add(new StringContent(model), "model");
                            httpContent.Add(new ByteArrayContent(bytes), file.Name, file.FileName);

                            client.BaseAddress = new Uri(uriString: "https://api.openai.com");

                            client.DefaultRequestHeaders.Add(name: "authorization", value: $"Bearer {_appKey}");
                            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("multipart/form-data"));//ACCEPT header

                            HttpResponseMessage response = client.PostAsync("/v1/audio/transcriptions", httpContent).Result;
                            string result = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync())!.text;
                            return Json(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(((int)HttpStatusCode.InternalServerError), ex.Message);
            }
        }

    }
}
