namespace Jellyfin.Plugin.Lastfm
{
    using Api;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Model.Serialization;
    using MediaBrowser.Model.Services;
    using Microsoft.Extensions.Logging;

    [Route("/Lastfm/Login", "POST")]
    public class Login
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class RestApi : IService
    {
        private readonly LastfmApiClient _apiClient;
        private readonly ILogger _logger;

        public RestApi(IJsonSerializer jsonSerializer, IHttpClient httpClient, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("AutoOrganize");
            _apiClient = new LastfmApiClient(httpClient, jsonSerializer, _logger);
        }

        public object Post(Login request)
        {
            return _apiClient.RequestSession(request.Username, request.Password).Result;
        }
    }
}
