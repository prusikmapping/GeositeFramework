﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GeositeFramework.Helpers;
using Newtonsoft.Json.Linq;

namespace GeositeFramework.Models
{
    /// <summary>
    /// View model for creating the main geosite view (Views/Home/Index.cshtml).
    /// </summary>
    public class Geosite
    {
        // For backwards compatibility with V1 region.json files,
        // provide defaults for the customized colors
        private readonly Color _defaultPrimary = ColorTranslator.FromHtml("#26648E");
        private readonly Color _defaultSecondary = ColorTranslator.FromHtml("#26648E");

        public class Link
        {
            public string Text;
            public string Url;

            /// <summary>
            /// Should this link generate a popup window with the url
            /// </summary>
            public bool Popup;

            /// <summary>
            /// Id of this launchpad this item should launch.
            /// If this value is not null, that means this item
            /// is a launchpad trigger.
            /// </summary>
            public string LaunchpadId;

            /// <summary>
            /// Resulting <a> tag will receive this id
            /// </summary>
            public string ElementId;

            /// <summary>
            /// List of child link signifies a dropdown menu
            /// </summary>
            public IList<Link> Items;
        }

        // Properties used in View rendering
        public string GeositeFrameworkVersion { get; set; }
        public string GoogleAnalyticsPropertyId { get; private set; }
        public Link TitleMain { get; private set; }
        public Link TitleDetail { get; private set; }
        public List<Link> HeaderLinks { get; private set; }
        public List<Link> RegionLinks { get; private set; }
        public string RegionDataJson { get; private set; }
        public List<string> PluginFolderNames { get; private set; }
        public string PluginModuleIdentifiers { get; private set; }
        public string PluginVariableNames { get; private set; }
        public List<string> PluginCssUrls { get; private set; }
        public string ConfigurationForUseJs { get; private set; }
        public String PrimaryColor { get; private set; }
        public String SecondaryColor { get; private set; }

        /// <summary>
        /// Create a Geosite object by loading the "region.json" file and enumerating plug-ins, using the specified paths.
        /// </summary>
        public static Geosite LoadSiteData(string regionJsonFilePath, string basePath, string appDataFolderPath)
        {
            var jsonDataRegion = new JsonDataRegion(appDataFolderPath).LoadFile(regionJsonFilePath);
            var pluginDirectories = PluginLoader.GetPluginDirectories(jsonDataRegion, basePath);
            PluginLoader.VerifyDirectoriesExist(pluginDirectories);
            var pluginFolderPaths = pluginDirectories.SelectMany(path => Directory.EnumerateDirectories(path));
            var pluginConfigData = GetPluginConfigurationData(pluginFolderPaths, appDataFolderPath);
            var pluginFolderNames = pluginFolderPaths.Select(path => PluginLoader.GetPluginFolderPath(basePath, path)).ToList();
            var pluginModuleNames = pluginFolderPaths.Select(path => PluginLoader.GetPluginModuleName(basePath, path)).ToList();
            var geosite = new Geosite(jsonDataRegion, pluginFolderNames, pluginModuleNames, pluginConfigData);
            return geosite;
        }

        private static List<JsonData> GetPluginConfigurationData(IEnumerable<string> pluginFolderPaths, string appDataFolderPath)
        {
            var pluginJsonFilename = "plugin.json";
            var pluginConfigData = new List<JsonData>();
            foreach (var path in pluginFolderPaths)
            {
                // Make sure main.js exists
                if (!File.Exists(Path.Combine(path, "main.js")))
                {
                    throw new ApplicationException("Missing 'main.js' file in plugin folder: " + path);
                }

                // Get plugin.json data if present
                var pluginJsonPath = Path.Combine(path, pluginJsonFilename);
                if (File.Exists(pluginJsonPath))
                {
                    var jsonData = new JsonDataPlugin(appDataFolderPath).LoadFile(pluginJsonPath);
                    pluginConfigData.Add(jsonData);
                }
            }
            return pluginConfigData;
        }

        /// <summary>
        /// Make a Geosite object given the specified configuration info.
        /// Note this is public only for testing purposes.
        /// </summary>
        /// <param name="jsonDataRegion">JSON configuration data (e.g. from a "region.json" configuration file)</param>
        /// <param name="existingPluginFolderNames">A list of plugin folder names (not full paths -- relative to the site "plugins" folder)</param>
        public Geosite(JsonData jsonDataRegion, List<string> existingPluginFolderNames,
            List<string> pluginModuleNames, List<JsonData> pluginConfigJsonData)
        {
            // Validate the region configuration JSON
            var jsonObj = jsonDataRegion.Validate();

            // Get plugin folder names, in the specified order
            if (jsonObj["pluginOrder"] != null)
            {
                List<string> pluginOrder = jsonObj["pluginOrder"].Select(t => (string)t).ToList();
                Func<string, int> byPluginOrder = x => pluginOrder.IndexOf(PluginLoader.StripPluginModule(x));
                PluginLoader.SortPluginNames(existingPluginFolderNames, byPluginOrder);
                PluginLoader.SortPluginNames(pluginModuleNames, byPluginOrder);
            }

            PluginFolderNames = existingPluginFolderNames;

            // Augment the JSON so the client will have the full list of folder names
            jsonObj.Add("pluginFolderNames", new JArray(PluginFolderNames.ToArray()));

            // Set public properties needed for View rendering
            TitleMain = ExtractLinkFromJson(jsonObj["titleMain"]);
            TitleDetail = ExtractLinkFromJson(jsonObj["titleDetail"]);

            var colorConfig = jsonObj["colors"];
            if (colorConfig != null)
            {
                PrimaryColor = ExtractColorFromJson(colorConfig, "primary");
                SecondaryColor = ExtractColorFromJson(colorConfig, "secondary");
            }
            else
            {
                PrimaryColor = ColorTranslator.ToHtml(_defaultPrimary);
                SecondaryColor = ColorTranslator.ToHtml(_defaultSecondary);
            }


            if (jsonObj["googleAnalyticsPropertyId"] != null)
            {
                GoogleAnalyticsPropertyId = (string)jsonObj["googleAnalyticsPropertyId"];
            }

            if (jsonObj["headerLinks"] != null)
            {
                HeaderLinks = jsonObj["headerLinks"]
                    .Select(ExtractLinkFromJson).ToList();
            }

            if (jsonObj["regionLinks"] != null)
            {
                RegionLinks = jsonObj["regionLinks"]
                    .Select(ExtractLinkFromJson).ToList();
            }

            // JSON to be inserted in generated JavaScript code
            RegionDataJson = jsonObj.ToString();

            // Create plugin module identifiers, to be inserted in generated JavaScript code. Example:
            //     "'plugins/layer_selector/main', 'plugins/measure/main'"
            PluginModuleIdentifiers = "'" + string.Join("', '", pluginModuleNames) + "'";

            // Create plugin variable names, to be inserted in generated JavaScript code. Example:
            //     "p0, p1"
            PluginVariableNames = string.Join(", ", PluginFolderNames.Select((name, i) => string.Format("p{0}", i)));

            if (pluginConfigJsonData != null)
            {
                MergePluginConfigurationData(this, pluginConfigJsonData);
            }
        }

        /// <summary>
        /// Validate the color syntax and return an HTML acceptable version
        /// of the specified color
        /// </summary>
        /// <param name="json">JSON Color entry from the config</param>
        /// <param name="key">Which color key to extract.</param>
        /// <returns>HEX/HTML color code from config</returns>
        private String ExtractColorFromJson(JToken json, string key)
        {
            try
            {
                // Run the values through the type system to provide meaningful
                // errors to syntax problems, since these values are essentially
                // getting tossed into the web page as code
                var color =  ColorTranslator.FromHtml(json[key].ToString());
                return ColorTranslator.ToHtml(color);
            }
            catch (Exception e)
            {
                const string msg = "Bad color config for key: `{0}`. Please use Hex (HTML) notation, ex. #FFCC66";
                throw new Exception(String.Format(msg, key), e);
            }
        }

        /// <summary>
        /// For a json specified link hash (tex/url) return a Link object
        /// </summary>
        private static Link ExtractLinkFromJson(JToken json)
        {
            return new Link
            {
                Text = (string)json["text"],
                Url = (string)json["url"],
                Popup = json["popup"] != null && bool.Parse(json["popup"].ToString()),
                LaunchpadId = json["launchpadId"] != null ? (string)json["launchpadId"] : null,
                ElementId = json["elementId"] != null ? (string)json["elementId"] : null,
                Items = ExtractLinkListFromJson(json)
            };
        }

        private static List<Link> ExtractLinkListFromJson(JToken json)
        {
            return json["items"] != null
                ? json["items"].Select(ExtractLinkFromJson).ToList()
                : new List<Link>();
        }

        // Example plugin.json file:
        // {
        //     css: [
        //         "plugins/layer_selector/main.css",
        //         "//cdn.sencha.io/ext-4.1.1-gpl/resources/css/ext-all.css"
        //     ],
        //     use: {
        //         underscore: { attach: "_" },
        //         extjs: { attach: "Ext" }
        //     }
        // }

        private static void MergePluginConfigurationData(Geosite geosite, List<JsonData> pluginConfigData)
        {
            var cssUrls = new List<string>();
            var useClauses = new List<string>();
            var useClauseDict = new Dictionary<string, string>();
            foreach (var jsonData in pluginConfigData)
            {
                // Parse and validate the plugin's JSON configuration data
                var jsonObj = jsonData.Validate();

                JToken token;
                if (jsonObj.TryGetValue("css", out token))
                {
                    // This config has CSS urls - add them to the list
                    cssUrls.AddRange(token.Values<string>());
                }
                if (jsonObj.TryGetValue("use", out token))
                {
                    // This config has "use" clauses - add unique ones to the list
                    foreach (JProperty p in token.Children())
                    {
                        var value = Regex.Replace(p.Value.ToString(), @"\s", ""); // remove whitespace
                        if (useClauseDict.ContainsKey(p.Name))
                        {
                            if (useClauseDict[p.Name] != value)
                            {
                                var message = string.Format(
                                    "Plugins define 'use' clause '{0}' differently: '{1}' vs. '{2}'",
                                    p.Name, value, useClauseDict[p.Name]);
                                throw new ApplicationException(message);
                            }
                        }
                        else
                        {
                            useClauseDict[p.Name] = value;
                            useClauses.Add(p.ToString());
                        }
                    }
                }
            }
            geosite.PluginCssUrls = cssUrls;
            geosite.ConfigurationForUseJs = string.Join("," + Environment.NewLine, useClauses);
        }

    }
}
