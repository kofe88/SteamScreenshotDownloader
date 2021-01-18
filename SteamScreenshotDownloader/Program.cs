using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SteamScreenshotDownloader {
    public class SteamScreenshot {
        public double FileId { get; set; }

        public string ScreenshotFilename { get; set; }

        public string ScreenshotUrl { get; set; }

        //public string ThumbnailFilename { get; set; }

        public string ThumbnailUrl { get; set; }
    }

    internal static class Program {
        private static string BaseDirectory { get; set; }

        private static void Main() {
            BaseDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException(), "FromSteam");

            Console.Write("Enter Steam ID: ");
            var steamId = Console.ReadLine();

            var screenshots = GetFileIdAndThumbnails(new List<SteamScreenshot>(), steamId, 1);

            foreach (var screenshot in screenshots) {
                var fullscreenUrl = GetFileActualUrl(screenshot.FileId);

                if (!string.IsNullOrWhiteSpace(fullscreenUrl)) {
                    screenshot.ScreenshotUrl = fullscreenUrl;
                }
            }

            SaveScreenshots(screenshots);

            Console.WriteLine("Done! Processed {0} screenshots, press any key to exit.", screenshots.Count);
            Console.Read();
        }

        private static WebResponse TryGetResponse(WebRequest request, int maxRetries = 10, int retryWaitSeconds = 3) {
            WebResponse result = null;
            var tries = 0;

            do {
                try {
                    tries++;
                    result = request.GetResponse();
                } catch {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Web request error, attempt {0}", tries);
                    Console.ResetColor();

                    System.Threading.Thread.Sleep(retryWaitSeconds * 1000);
                }
            } while (result == null && tries <= maxRetries);
            return result;
        }

        private static void SaveScreenshots(IEnumerable<SteamScreenshot> screenshots) {
            const string dispositionPattern = @"inline; filename(?:\*\=UTF-8'')(?<Filename>.*?);|inline; filename=""(?<Filename>.*?)"";";

            foreach (var screenshot in screenshots) {
                if (string.IsNullOrWhiteSpace(screenshot.ScreenshotUrl)) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Screenshot Url is not valid for File Id: {0}", screenshot.FileId);
                    Console.ResetColor();
                    continue;
                }

                var screenshotWebRequest = WebRequest.Create(screenshot.ScreenshotUrl) as HttpWebRequest;

                using (var response = TryGetResponse(screenshotWebRequest)) {
                    // TODO: Handle null responses...
                    if (response == null) continue;
                    using (var stream = response.GetResponseStream()) {
                        if (response.Headers.AllKeys.Any(x => x.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))) {
                            var fullDisposition = response.Headers["Content-Disposition"];
                            var regexMatch = Regex.Match(fullDisposition, dispositionPattern);

                            if (regexMatch.Success) {
                                var fullFilename = regexMatch.Groups["Filename"].Value;

                                // Underscores are used as folder separators, except the last one, which splits the date & time values
                                var lastUnderscore = fullFilename.LastIndexOf('_');
                                fullFilename = fullFilename.Substring(0, lastUnderscore).Replace('_', '\\') + fullFilename.Substring(lastUnderscore, fullFilename.Length - lastUnderscore);
                                fullFilename = fullFilename.Replace("screenshots", "");
                                var lastSlash = fullFilename.LastIndexOf('\\');
                                fullFilename = fullFilename.Substring(0, lastSlash - 1);
                                screenshot.ScreenshotFilename = fullFilename + @"\\" + screenshot.FileId + ".jpg";
                            } else {
                                screenshot.ScreenshotFilename = screenshot.FileId + ".jpg";
                            }
                        }

                        var screenshotFilename = screenshot.ScreenshotFilename;
                        if (string.IsNullOrEmpty(screenshotFilename)) {
                            screenshotFilename = screenshot.FileId + ".jpg";
                        }
                        var fullScreenshotFilePath = Path.Combine(BaseDirectory, screenshotFilename);
                        var fullScreenshotDirectoryPath = Path.GetDirectoryName(fullScreenshotFilePath);

                        if (!Directory.Exists(fullScreenshotDirectoryPath)) {
                            Directory.CreateDirectory(fullScreenshotDirectoryPath ?? throw new InvalidOperationException());
                        }

                        using (var fileStream = new FileStream(fullScreenshotFilePath, FileMode.Create)) {
                            Console.WriteLine("Saving screenshot to {0}", fullScreenshotFilePath);
                            stream?.CopyTo(fileStream);
                        }
                    }
                }

                /*if (string.IsNullOrWhiteSpace(screenshot.ThumbnailUrl)) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Thumbnail Url is not valid for File Id: {0}", screenshot.FileId);
                    Console.ResetColor();
                    continue;
                }

                var thumbnailWebRequest = WebRequest.Create(screenshot.ThumbnailUrl) as HttpWebRequest;

                using (var response = TryGetResponse(thumbnailWebRequest)) {
                    //   TODO: Handle null responses...
                    if (response == null) continue;
                    using (var stream = response.GetResponseStream()) {
                        if (response.Headers.AllKeys.Any(x => x.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))) {
                            var fullDisposition = response.Headers["Content-Disposition"];
                            var regexMatch = Regex.Match(fullDisposition, dispositionPattern);

                            if (regexMatch.Success) {
                                var fullFilename = regexMatch.Groups["Filename"].Value;

                                var lastUnderscore = fullFilename.LastIndexOf('_');

                                fullFilename = fullFilename.Substring(0, lastUnderscore).Replace('_', '\\') + fullFilename.Substring(lastUnderscore, fullFilename.Length - lastUnderscore);

                                screenshot.ThumbnailFilename = fullFilename;
                            }
                        } else {
                            screenshot.ThumbnailFilename = $"t_{screenshot.FileId}.jpg";
                        }

                        var fullScreenshotFilePath = Path.Combine(BaseDirectory, screenshot.ThumbnailFilename);
                        var fullScreenshotDirectoryPath = Path.GetDirectoryName(fullScreenshotFilePath);

                        if (!Directory.Exists(fullScreenshotDirectoryPath)) {
                            Directory.CreateDirectory(fullScreenshotDirectoryPath ?? throw new InvalidOperationException());
                        }

                        using (var fileStream = new FileStream(fullScreenshotFilePath, FileMode.Create)) {
                            Console.WriteLine("Saving thumbnail to {0}", fullScreenshotFilePath);
                            stream?.CopyTo(fileStream);
                        }
                    }
                }*/
            }
        }

        private static List<SteamScreenshot> GetFileIdAndThumbnails(List<SteamScreenshot> screenshots, string steamId, int pageNo) {
            Console.WriteLine("Getting file ids and thumbnails for {0}, page {1}", steamId, pageNo);
            var requestScreenshots = new List<SteamScreenshot>();

            const string fileDetailPattern = "<div style=\\\"background-image: url\\('(?<ThumbnailUrl>.*?)'\\);\\\" class=\\\"imgWallItem.*?\\\" id=\\\"imgWallItem_(?<FileId>\\d{1,})\\\"";
            var url = $"https://steamcommunity.com/id/{steamId}/screenshots/?p={pageNo}&sort=newestfirst&view=grid";
            MatchCollection fileIdMatches;
            var retries = 10;

            do {
                GC.Collect();
                var webRequest = WebRequest.Create(url) as HttpWebRequest;
                var response = webRequest?.GetResponse();
                // TODO: Handle null responses...
                var stream = response?.GetResponseStream();
                var streamReader = new StreamReader(stream ?? throw new InvalidOperationException());
                var html = streamReader.ReadToEnd();
                fileIdMatches = Regex.Matches(html, fileDetailPattern, RegexOptions.IgnoreCase);
                retries--;
            } while (fileIdMatches.Count == 0 && retries > 0);

            for (var i = 0; i < fileIdMatches.Count; i++) {
                var match = fileIdMatches[i];
                var fileIdValue = double.Parse(match.Groups["FileId"].Value.Trim());
                var thumbnailUrlValue = match.Groups["ThumbnailUrl"].Value.Trim();
                var newScreenshot = new SteamScreenshot { FileId = fileIdValue, ThumbnailUrl = thumbnailUrlValue };

                Console.WriteLine("Added File Id: {0}, with Thumbnail Url: {1}", newScreenshot.FileId,
                    newScreenshot.ThumbnailUrl);

                requestScreenshots.Add(newScreenshot);
            }


            if (!requestScreenshots.Any()) return screenshots;
            screenshots.AddRange(requestScreenshots);
            GetFileIdAndThumbnails(screenshots, steamId, pageNo + 1);
            return screenshots;
        }

        private static string GetFileActualUrl(double fileId) {
            const string fileDetailUrlFormat = "https://steamcommunity.com/sharedfiles/filedetails/?id={0}";
            const string actualUrlPattern = @"href="".*?ugc.*?""";

            var fileDetailUrl = string.Format(fileDetailUrlFormat, fileId);

            var webRequest = WebRequest.Create(fileDetailUrl) as HttpWebRequest;

            using (var response = TryGetResponse(webRequest)) {
                // TODO: Handle null responses...
                if (response == null) return null;
                using (var stream = response.GetResponseStream())
                using (var streamReader = new StreamReader(stream ?? throw new InvalidOperationException())) {
                    var html = streamReader.ReadToEnd();

                    var actualFileUrlMatches = Regex.Matches(html, actualUrlPattern, RegexOptions.IgnoreCase);

                    if (actualFileUrlMatches.Count <= 0) return null;
                    var match = actualFileUrlMatches[1];
                    var value = match.Value.Replace("href=", "").Replace("\"", "").Trim();

                    Console.WriteLine("Found screenshot url {0} for File Id: {1}", value, fileId);

                    return value;
                }
            }
        }
    }
}
