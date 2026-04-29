using System;
using System.Net;
using System.Net.Http;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Centralized factory for HttpClient instances configured to honor the
    /// system proxy. Using one factory keeps timeouts, proxy, and TLS
    /// settings consistent across blocklist downloads and update checks.
    /// </summary>
    public static class HttpClientFactory
    {
        public static HttpClient Create(TimeSpan? timeout = null)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = WebRequest.GetSystemWebProxy(),
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };

            // Use system credentials so authenticated corporate proxies work.
            if (handler.Proxy != null)
            {
                handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
            }

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GoodbyeAhmetWPF/1.0");
            return client;
        }
    }
}
