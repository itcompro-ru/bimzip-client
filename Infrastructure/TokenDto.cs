using Newtonsoft.Json;

namespace BimZipClient.Infrastructure
{
    public class TokenDto
    {
        [JsonProperty(Required = Required.Always)]
        public string AccessToken { get; set; }
    }
}