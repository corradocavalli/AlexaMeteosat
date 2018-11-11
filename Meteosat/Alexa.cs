#region Using

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alexa.NET;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Alexa.NET.Response.Directive;
using Alexa.NET.Response.Directive.Templates;
using Alexa.NET.Response.Directive.Templates.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

#endregion

namespace Meteosat
{
    public static class Alexa
    {
        private static readonly Dictionary<string, string> infos = new Dictionary<string, string>();

        static Alexa()
        {
            infos.Add("Italia", "IT");
            infos.Add("Alpi", "ALPS");
            infos.Add("Europa", "EU");
            infos.Add("Germania", "DE");
            infos.Add("Francia", "FR");
            infos.Add("Spagna", "SP");
            infos.Add("Gran Bretagna", "GB");
            infos.Add("Russia", "NL");
            infos.Add("Polonia", "PL");
            infos.Add("Grecia", "GR");
            infos.Add("Turchia", "TU");
            infos.Add("Balcani", "BA");
            infos.Add("Ungheria", "HU");
            infos.Add("Olanda", "NL");
            infos.Add("Scandinavia", "SCAN");
        }

        [FunctionName("AlexaMeteosat")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)]
            HttpRequest req, TraceWriter log)
        {
            string json = await req.ReadAsStringAsync();
            SkillRequest skillRequest = JsonConvert.DeserializeObject<SkillRequest>(json);
            bool isValid = await Alexa.ValidateRequest(req, skillRequest);
            if (!isValid)
            {
                return new BadRequestResult();
            }

            //We check if invoking device supports diplay
            if (!skillRequest.Context.System.Device.IsInterfaceSupported("Display"))
            {
                var notSupportedResponse = ResponseBuilder.Tell("Mi spiace, questa skill é supportata solo da dispositivi muniti di schermo.");
                return new OkObjectResult(notSupportedResponse);
            }

            var requestType = skillRequest.GetRequestType();

            SkillResponse response = null;

            if (requestType == typeof(LaunchRequest))
            {
                response = Alexa.CreateResponse(false);
            }
            else if (requestType == typeof(IntentRequest))
            {
                var intentRequest = skillRequest.Request as IntentRequest;
                switch (intentRequest.Intent.Name)
                {
                    case "infrared":
                    case "AMAZON.NavigateHomeIntent":
                        response = Alexa.CreateResponse(true);
                        break;
                    case "normal":
                        response = Alexa.CreateResponse(false);
                        break;
                    case "AMAZON.StopIntent":
                    case "AMAZON.CancelIntent":
                        response = Alexa.CreateGoodbyeResponse();
                        break;
                    case "AMAZON.HelpIntent":
                        response = Alexa.CreateHelpResponse();
                        break;
                    case "AMAZON.NextIntent":
                        response = Alexa.CreteScrollResponse(false);
                        break;
                    case "AMAZON.PreviousIntent":
                        response = Alexa.CreteScrollResponse(true);
                        break;
                    default:
                        response = ResponseBuilder.Empty();
                        response.Response.ShouldEndSession = false;
                        break;
                }
            }
            else if (requestType == typeof(SessionEndedRequest))
            {
                response = Alexa.CreateGoodbyeResponse();
                response.Response.ShouldEndSession = true;
            }

            return new OkObjectResult(response);
        }

        private static SkillResponse CreateHelpResponse()
        {
            string help = "Puoi dire 'notte' o 'infrarosso' per visualizzare le immagini all'infrarosso oppure 'giorno' o 'normale' per la visione diurna.";
            var response = ResponseBuilder.TellWithCard("Ecco le istruzioni",
                "Aiuto", help);
            response.Response.OutputSpeech = new PlainTextOutputSpeech {Text = help};
            response.Response.ShouldEndSession = false;
            return response;
        }

        /// <summary>
        /// Creates the metosat response.
        /// </summary>
        /// <param name="infrared">if set to <c>true</c> [infrared].</param>
        /// <returns></returns>
        private static SkillResponse CreateResponse(bool infrared)
        {
            string text = infrared ? "Ecco le ultime immagini all' infraross dal satellite meteosàt" : "Ecco le ultime immagini dal satellite meteosàt";
            SkillResponse response = ResponseBuilder.Tell(text);
            DisplayRenderTemplateDirective display = new DisplayRenderTemplateDirective();

            var bodyTemplate = new ListTemplate2
            {
                Title = "Immagini meteosat",
                BackButton = "HIDDEN"
            };

            foreach (KeyValuePair<string, string> info in infos)
            {
                var image = new TemplateImage() {ContentDescription = $"Vista {info.Key}"};

                string url = infrared ? $"https://api.sat24.com/mostrecent/{info.Value}/infraPolair" : $"https://api.sat24.com/mostrecent/{info.Value}/visual5hdcomplete";

                image.Sources.Add(new ImageSource()
                {
                    Url = url,
                    Height = 615,
                    Width = 845,
                });

                ListItem item = new ListItem
                {
                    Image = image,
                    Content = new TemplateContent
                    {
                        Primary = new TemplateText()
                        {
                            Text = $"{info.Key}",
                            Type = "PlainText"
                        }
                    }
                };

                bodyTemplate.Items.Add(item);
            }

            display.Template = bodyTemplate;
            response.Response.Directives.Add(display);
            response.Response.ShouldEndSession = false;
            return response;
        }

        private static SkillResponse CreateGoodbyeResponse()
        {
            return ResponseBuilder.Tell("Arrivederci!");
        }

        private static SkillResponse CreteScrollResponse(bool back)
        {
            string text = back ? "Fai scorrere lo schermo verso destra per l'immagine precedente" : "Fai scorrere lo schermo verso sinistra per l'immagine successiva";
            var response = ResponseBuilder.Tell(text);
            response.Response.ShouldEndSession = false;
            return response;
        }

        /// <summary>
        /// Validates the request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="skillRequest">The skill request.</param>
        /// <returns></returns>
        private static async Task<bool> ValidateRequest(HttpRequest request, SkillRequest skillRequest)
        {
            request.Headers.TryGetValue("SignatureCertChainUrl", out var signatureChainUrl);
            if (string.IsNullOrWhiteSpace(signatureChainUrl))
            {
                return false;
            }

            Uri certUrl;
            try
            {
                certUrl = new Uri(signatureChainUrl);
            }
            catch
            {
                return false;
            }

            request.Headers.TryGetValue("Signature", out var signature);
            if (string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            request.Body.Position = 0;
            var body = await request.ReadAsStringAsync();
            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            bool valid = await RequestVerification.Verify(signature, certUrl, body);
            bool isTimestampValid = RequestVerification.RequestTimestampWithinTolerance(skillRequest);

            if (!isTimestampValid)
            {
                valid = false;
            }

            return valid;
        }
    }
}