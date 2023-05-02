﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Bloxstrap.Models;
using DiscordRPC;

namespace Bloxstrap.Helpers
{
    public class DeployManager
    {
        #region Properties
        public const string DefaultChannel = "LIVE";

        private string _channel = DefaultChannel;

        public string Channel
        {
            get => _channel;
            set
            {
                if (_channel != value)
                    App.Logger.WriteLine($"[DeployManager::SetChannel] Changed channel to {value}");

                _channel = value;
            }
        }

        // a list of roblox delpoyment locations that we check for, in case one of them don't work
        private List<string> BaseUrls = new()
        {
            "https://setup.rbxcdn.com",
            "https://setup-ak.rbxcdn.com",
            "https://s3.amazonaws.com/setup.roblox.com"
        };

        private string? _baseUrl = null;

        public string BaseUrl
        {
            get
            {
                if (String.IsNullOrEmpty(_baseUrl))
                {
                    // check for a working accessible deployment domain
                    foreach (string attemptedUrl in BaseUrls)
                    {
                        App.Logger.WriteLine($"[DeployManager::DefaultBaseUrl.Set] Testing connection to '{attemptedUrl}'...");

                        try
                        {
                            App.HttpClient.GetAsync($"{attemptedUrl}/version").Wait();
                            App.Logger.WriteLine($"[DeployManager::DefaultBaseUrl.Set] Connection successful!");
                            _baseUrl = attemptedUrl;
                            break;
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine($"[DeployManager::DefaultBaseUrl.Set] Connection failed!");
                            App.Logger.WriteLine($"[DeployManager::DefaultBaseUrl.Set] {ex}");
                            continue;
                        }
                    }

                    if (String.IsNullOrEmpty(_baseUrl))
                        throw new Exception("Unable to find an accessible Roblox deploy mirror!");
                }

                if (Channel == DefaultChannel)
                    return _baseUrl; 
                else
                    return $"{_baseUrl}/channel/{Channel.ToLower()}";
            }
        }

        // most commonly used/interesting channels


        public static List<string> SelectableChannels = null;
        #endregion

        public static async Task Initialize()
        {
           SelectableChannels = await DeployHelper.GetChannels();
        }

        public async Task<ClientVersion> GetLastDeploy(bool timestamp = false)
        {
            App.Logger.WriteLine($"[DeployManager::GetLastDeploy] Getting deploy info for channel {Channel} (timestamp={timestamp})");

            HttpResponseMessage deployInfoResponse = await App.HttpClient.GetAsync($"https://clientsettings.roblox.com/v2/client-version/WindowsPlayer/channel/{Channel}").ConfigureAwait(false);

            string rawResponse = await deployInfoResponse.Content.ReadAsStringAsync();

            if (!deployInfoResponse.IsSuccessStatusCode)
            {
                // 400 = Invalid binaryType.
                // 404 = Could not find version details for binaryType.
                // 500 = Error while fetching version information.
                // either way, we throw
                
                App.Logger.WriteLine(
                    "[DeployManager::GetLastDeploy] Failed to fetch deploy info!\r\n"+ 
                    $"\tStatus code: {deployInfoResponse.StatusCode}\r\n"+ 
                    $"\tResponse: {rawResponse}"
                );

                throw new Exception($"Could not get latest deploy for channel {Channel}! (HTTP {deployInfoResponse.StatusCode})");
            }

            App.Logger.WriteLine($"[DeployManager::GetLastDeploy] Got JSON: {rawResponse}");

            ClientVersion clientVersion = JsonSerializer.Deserialize<ClientVersion>(rawResponse)!;

            // for preferences
            if (timestamp)
            {
                App.Logger.WriteLine("[DeployManager::GetLastDeploy] Getting timestamp...");

                string manifestUrl = $"{BaseUrl}/{clientVersion.VersionGuid}-rbxPkgManifest.txt";

                // get an approximate deploy time from rbxpkgmanifest's last modified date
                HttpResponseMessage pkgResponse = await App.HttpClient.GetAsync(manifestUrl);

                if (pkgResponse.Content.Headers.TryGetValues("last-modified", out var values))
                {
                    string lastModified = values.First();
                    App.Logger.WriteLine($"[DeployManager::GetLastDeploy] {manifestUrl} - Last-Modified: {lastModified}");
                    clientVersion.Timestamp = DateTime.Parse(lastModified).ToLocalTime();
                }
            }

            return clientVersion;
        }
    }
}
