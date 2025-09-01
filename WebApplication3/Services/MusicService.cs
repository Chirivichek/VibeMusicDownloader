using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders.Physical;
using Newtonsoft.Json.Linq;
using RussianTransliteration;
using WebApplication3.Pages;
namespace WebApplication3.Services
{
    public class MusicService
    {
        private readonly HttpClient _httpClient;
        private readonly SpotifyService _spotifyService; 

        public bool CheckDownloadDirectory()
        {
            // Используйте абсолютный путь
            var downloadDirectory = @"D:\VSproject\WebApplication3\WebApplication3\wwwroot\music";

            try
            {
                if (!Directory.Exists(downloadDirectory))
                {
                    Directory.CreateDirectory(downloadDirectory);
                    Console.WriteLine($"Created directory: {downloadDirectory}");
                }

                // Проверяем права на запись
                var testFile = Path.Combine(downloadDirectory, "test_write.txt");
                File.WriteAllText(testFile, "Test write operation");
                File.Delete(testFile);

                Console.WriteLine($"Directory is writable: {downloadDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Directory error: {ex.Message}");
                return false;
            }
        }
        public MusicService(HttpClient httpClient, SpotifyService spotifyService)
        {
            _httpClient = httpClient;
            _spotifyService = spotifyService;
        }
        public async Task<List<TrackInfo>> SearchDeezerAsync(string query)
        {
            var result = new List<TrackInfo>();

            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var searchUrl = $"https://api.deezer.com/search?q={encodedQuery}&limit=20";

                var response = await _httpClient.GetStringAsync(searchUrl);
                var json = JObject.Parse(response);

                if (json["data"] != null)
                {
                    foreach (var item in json["data"])
                    {
                        result.Add(new TrackInfo
                        {
                            Title = item["title"]?.ToString(),
                            Artist = item["artist"]?["name"]?.ToString(),
                            DeezerId = item["id"]?.ToString(),
                            DeezerUrl = item["link"]?.ToString(),
                            IsAvailable = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching Deezer: {ex.Message}");
            }

            return result;
        }

        public async Task<TrackInfo?> GetDeezerTrackById(string deezerId)
        {
            try
            {
                var trackUrl = $"https://api.deezer.com/track/{deezerId}";
                var response = await _httpClient.GetStringAsync(trackUrl);
                var json = JObject.Parse(response);

                return new TrackInfo
                {
                    Title = json["title"]?.ToString(),
                    Artist = json["artist"]?["name"]?.ToString(),
                    DeezerId = json["id"]?.ToString(),
                    DeezerUrl = json["link"]?.ToString(),
                    IsAvailable = true
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting Deezer track by ID: {ex.Message}");
                return null;
            }
        }
        // Метод для получения ID трека в Deezer
        public async Task<string?> GetDeezerTrackId(string title, string artist, string? album = null, int? durationMs = null)
        {
            try
            {
                Console.WriteLine($"Searching Deezer for: {artist} - {title}");

                // 1. Пробуем оригинальный поиск
                var deezerId = await SearchDeezerWithQuery(title, artist, album, durationMs);
                if (deezerId != null) return deezerId;

                // 2. Пробуем транслитерировать английские названия в русские
                var russianTitle = RussianTransliterator.GetTransliteration(title);
                var russianArtist = RussianTransliterator.GetTransliteration(artist);

                if (russianTitle != title || russianArtist != artist)
                {
                    deezerId = await SearchDeezerWithQuery(russianTitle, russianArtist, album, durationMs);
                    if (deezerId != null) return deezerId;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeezerTrackId: {ex.Message}");
                return null;
            }
        }
        public async Task<DeezerPlaylistInfo?> GetDeezerPlaylistInfo(string playlistId)
        {
            try
            {
                var playlistUrl = $"https://api.deezer.com/playlist/{playlistId}";
                var response = await _httpClient.GetStringAsync(playlistUrl);
                var json = JObject.Parse(response);

                return new DeezerPlaylistInfo
                {
                    Title = json["title"]?.ToString(),
                    Description = json["description"]?.ToString(),
                    ImageUrl = json["picture_medium"]?.ToString(),
                    Owner = json["creator"]?["name"]?.ToString(),
                    TrackCount = json["nb_tracks"]?.ToObject<int>() ?? 0,
                    Url = json["link"]?.ToString()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting Deezer playlist info: {ex.Message}");
                return null;
            }
        }

        public async Task<List<DeezerTrack>> GetDeezerPlaylistTracks(string playlistId)
        {
            var tracks = new List<DeezerTrack>();

            try
            {
                var tracksUrl = $"https://api.deezer.com/playlist/{playlistId}/tracks";
                var response = await _httpClient.GetStringAsync(tracksUrl);
                var json = JObject.Parse(response);

                if (json["data"] != null)
                {
                    foreach (var item in json["data"])
                    {
                        tracks.Add(new DeezerTrack
                        {
                            Title = item["title"]?.ToString(),
                            Artist = item["artist"]?["name"]?.ToString(),
                            DeezerId = item["id"]?.ToString(),
                            DeezerUrl = item["link"]?.ToString()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting Deezer playlist tracks: {ex.Message}");
            }

            return tracks;
        }

        private async Task<string?> SearchDeezerWithQuery(string title, string artist, string? album, int? durationMs)
        {

            try
            {
                var cleanTitle = CleanTrackTitle(title);
                var cleanArtist = CleanArtistName(artist);

                var searchQuery = $"artist:\"{cleanArtist}\" track:\"{cleanTitle}\"";
                if (!string.IsNullOrEmpty(album))
                {
                    searchQuery += $" album:\"{CleanAlbumName(album)}\"";
                }

                var encodedQuery = Uri.EscapeDataString(searchQuery);
                var searchUrl = $"https://api.deezer.com/search?q={encodedQuery}&limit=5";

                var response = await _httpClient.GetStringAsync(searchUrl);
                var json = JObject.Parse(response);

                if (json["data"] == null || !json["data"].Any())
                    return null;

                var targetDurationSec = durationMs.HasValue ? (int?)(durationMs.Value / 1000) : null;

                // Ищем лучший матч по длительности
                foreach (var track in json["data"])
                {
                    var trackDuration = track["duration"]?.ToObject<int>() ?? 0;
                    if (targetDurationSec.HasValue && Math.Abs(trackDuration - targetDurationSec.Value) <= 5)
                    {
                        return track["id"]?.ToString();
                    }
                }

                // Если не нашли по длительности, возвращаем первый результат
                return json["data"][0]["id"]?.ToString();
            }
            catch
            {
                return null;
            }
        }


        private string CleanTrackTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;

            title = System.Text.RegularExpressions.Regex.Replace(title, @"\([^)]*\)", "").Trim();
            title = System.Text.RegularExpressions.Regex.Replace(title, @"\[[^\]]*\]", "").Trim();

            var toRemove = new[] { "- Radio Edit", "- Original Mix", "feat.", "ft.", "vs.", "(Official Video)", "[Official Audio]" };
            foreach (var remove in toRemove)
            {
                title = title.Replace(remove, "", StringComparison.OrdinalIgnoreCase);
            }

            return title.Trim();
        }

        private string CleanArtistName(string artist)
        {
            if (string.IsNullOrEmpty(artist)) return artist;

            var featIndex = artist.IndexOf("feat", StringComparison.OrdinalIgnoreCase);
            if (featIndex > 0) return artist.Substring(0, featIndex).Trim();

            featIndex = artist.IndexOf("ft", StringComparison.OrdinalIgnoreCase);
            if (featIndex > 0) return artist.Substring(0, featIndex).Trim();

            return artist.Trim();
        }

        private string CleanAlbumName(string album)
        {
            return CleanTrackTitle(album);
        }

        // Метод для получения ссылки на трек в Deezer
        public async Task<string?> GetDeezerTrackUrl(string title, string artist)
        {
            try
            {
                var trackId = await GetDeezerTrackId(title, artist);
                return trackId != null ? $"https://www.deezer.com/track/{trackId}" : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting Deezer track URL for {artist} - {title}: {ex.Message}");
                return null;
            }
        }
       
        public async Task<List<TrackInfo>> GetPlaylistTracksWithAvailability(string playlistId)
        {
            try
            {
                await _spotifyService.InitializeAsync(_spotifyService._clientId!, _spotifyService._clientSecret!);
                var spotifyTracks = await _spotifyService.GetPlaylistTrackAsync(playlistId);

                var trackInfos = new List<TrackInfo>();

                foreach (var track in spotifyTracks)
                {
                    var trackInfo = new TrackInfo
                    {
                        Title = track.Name,
                        Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
                        IsAvailable = true
                    };

                    try
                    {
                        // Проверяем доступность на Deezer
                        var deezerId = await GetDeezerTrackId(
                            track.Name,
                            string.Join(", ", track.Artists.Select(a => a.Name))
                            );

                        if (deezerId != null)
                        {
                            trackInfo.DeezerId = deezerId;
                            trackInfo.DeezerUrl = $"https://www.deezer.com/track/{deezerId}";
                        }
                        else
                        {
                            trackInfo.IsAvailable = false;
                            trackInfo.ErrorMessage = "Трек не найден в Deezer";
                        }
                    }
                    catch (Exception ex)
                    {
                        trackInfo.IsAvailable = false;
                        trackInfo.ErrorMessage = $"Ошибка проверки на Deezer: {ex.Message}";
                    }
                    trackInfos.Add(trackInfo);
                }
                return trackInfos;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error getting playlist tracks with availability: {ex.Message}");
            }

        }
        public async Task<IActionResult> DownloadTrackWithSaveDialog(string deezerUrl, string format = "mp3")
        {
            string pythonPath = @"C:\Users\pavlo\AppData\Local\Programs\Python\Python39\python.exe";
            var downloadDirectory = @"D:\VSproject\WebApplication3\WebApplication3\wwwroot\music";

            // Определяем битрейт по формату
            string bitrate = format.ToLower() == "flac" ? "9" : "3"; // 9 = FLAC, 3 = MP3 320

            // Скачиваем на сервер
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $" -m deemix -b {bitrate} -p \"{downloadDirectory}\" \"{deezerUrl}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) throw new Exception("Не удалось запустить Python.");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Deemix error: {error}");
            }

            // Ищем файл с правильным расширением
            string extension = format.ToLower() == "flac" ? ".flac" : ".mp3";
            var audioFiles = Directory.GetFiles(downloadDirectory, $"*{extension}")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();


            //// Находим скачанный файл
            //var audioFiles = Directory.GetFiles(downloadDirectory, "*.mp3")
            //    .Select(f => new FileInfo(f))
            //    .OrderByDescending(f => f.LastWriteTime)
            //    .ToList();

            if (audioFiles.Count == 0)
                throw new Exception($"No {format} files found after download\"");

            var file = audioFiles[0];

            // Читаем файл в память и возвращаем как FileResult
            // Это заставит браузер предложить "Сохранить как"
            var fileBytes = await System.IO.File.ReadAllBytesAsync(file.FullName);

            // Удаляем временный файл с сервера
            System.IO.File.Delete(file.FullName);

            return new FileContentResult(fileBytes, format.ToLower() == "flac" ? "audio/flac" : "audio/mpeg")
            {
                FileDownloadName = file.Name // Браузер предложит это имя
            };
        }
        public async Task<(IActionResult Result, List<TrackInfo> FailedTracks)> DownloadPlaylistAsync(string spotifyPlaylistId, string format = "mp3")
        {
            // Создаем временную директорию вне папки music
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"spotify_playlist_{spotifyPlaylistId}_{Guid.NewGuid().ToString("N").Substring(0, 8)}");
            Directory.CreateDirectory(tempDirectory);

            var failedTracks = new List<TrackInfo>();

            try
            {
                // Используем новый метод для получения треков с информацией о доступности
                var tracks = await GetPlaylistTracksWithAvailability(spotifyPlaylistId);

                if (tracks.Count == 0 || tracks == null)
                    throw new Exception("No tracks found in the Spotify playlist.");

                // 2. Скачиваем каждый трек отдельно
                var downloadedCount = 0;

                foreach (var track in tracks)
                {
                    try
                    {
                        if (track.IsAvailable && !string.IsNullOrEmpty(track.DeezerUrl))
                        {
                            var success = await DownloadSingleTrack(track.DeezerUrl!, tempDirectory, format);
                            if (success)
                            {
                                downloadedCount++;
                            }
                            else
                            {
                                track.IsAvailable = false;
                                track.ErrorMessage = "Ошибка скачивания";
                                failedTracks.Add(track);
                            }
                        }
                        else
                        {
                            failedTracks.Add(track);
                        }
                        await Task.Delay(1000); // Небольшая пауза между скачиваниями
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading {track.Title}: {ex.Message}");
                        track.IsAvailable = false;
                        track.ErrorMessage = ex.Message;
                        failedTracks.Add(track);
                    }
                }

                if (downloadedCount == 0)
                    throw new Exception("No tracks were downloaded from the Spotify playlist.");

                // Создаем zip в папке music
                var result = await CreateZipInMusicDirectory(tempDirectory, $"spotify_playlist_{spotifyPlaylistId}");
                return (result, failedTracks);
            }
            finally
            {
                // Всегда очищаем временную директорию
                try
                {
                    if (Directory.Exists(tempDirectory))
                        Directory.Delete(tempDirectory, true);
                }
                catch
                {
                    // Логируем ошибку, но не прерываем выполнение
                    Console.WriteLine("Failed to delete temp directory");
                }
            }
        }
        public async Task<(IActionResult Result, List<TrackInfo> FailedTracks)> DownloadDeezerPlaylistAsync(string deezerPlaylistId, string format = "mp3")
        {
            // Создаем временную директорию
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"deezer_playlist_{deezerPlaylistId}_{Guid.NewGuid().ToString("N").Substring(0, 8)}");
            Directory.CreateDirectory(tempDirectory);

            var failedTracks = new List<TrackInfo>();

            try
            {
                // Получаем треки из Deezer плейлиста
                var deezerTracks = await GetDeezerPlaylistTracks(deezerPlaylistId);

                if (deezerTracks.Count == 0)
                    throw new Exception("No tracks found in the Deezer playlist.");

                // Скачиваем каждый трек
                var downloadedCount = 0;

                foreach (var track in deezerTracks)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(track.DeezerUrl))
                        {
                            var success = await DownloadSingleTrack(track.DeezerUrl, tempDirectory, format);
                            if (success)
                            {
                                downloadedCount++;
                            }
                            else
                            {
                                var trackInfo = new TrackInfo
                                {
                                    Title = track.Title,
                                    Artist = track.Artist,
                                    DeezerId = track.DeezerId,
                                    DeezerUrl = track.DeezerUrl,
                                    IsAvailable = false,
                                    ErrorMessage = "Ошибка скачивания"
                                };
                                failedTracks.Add(trackInfo);
                            }
                        }
                        await Task.Delay(1000); // Пауза между скачиваниями
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading {track.Title}: {ex.Message}");
                        var trackInfo = new TrackInfo
                        {
                            Title = track.Title,
                            Artist = track.Artist,
                            DeezerId = track.DeezerId,
                            DeezerUrl = track.DeezerUrl,
                            IsAvailable = false,
                            ErrorMessage = ex.Message
                        };
                        failedTracks.Add(trackInfo);
                    }
                }

                if (downloadedCount == 0)
                    throw new Exception("No tracks were downloaded from the Deezer playlist.");

                // Создаем ZIP архив
                var result = await CreateZipInMusicDirectory(tempDirectory, $"deezer_playlist_{deezerPlaylistId}");
                return (result, failedTracks);
            }
            finally
            {
                // Очищаем временную директорию
                try
                {
                    if (Directory.Exists(tempDirectory))
                        Directory.Delete(tempDirectory, true);
                }
                catch
                {
                    Console.WriteLine("Failed to delete temp directory");
                }
            }
        }
        public async Task<bool> DownloadSingleTrack(string deezerUrl, string directoryPath, string format = "mp3")
        {
            string pythonPath = @"C:\Users\pavlo\AppData\Local\Programs\Python\Python39\python.exe";
            string bitrate = format.ToLower() == "flac" ? "9" : "3";

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $" -m deemix -b {bitrate} -p \"{directoryPath}\" \"{deezerUrl}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }

        private async Task<IActionResult> CreateZipInMusicDirectory(string sourceDirectory, string zipFileName)
        { // Создаем ZIP в памяти
            using var memoryStream = new MemoryStream();

            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                // Добавляем все файлы из временной директории в ZIP
                foreach (var filePath in Directory.GetFiles(sourceDirectory))
                {
                    var entryName = Path.GetFileName(filePath);
                    var entry = archive.CreateEntry(entryName);

                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(filePath);
                    await fileStream.CopyToAsync(entryStream);
                }
            }

            // Получаем байты из memory stream
            memoryStream.Position = 0;
            var fileBytes = memoryStream.ToArray();

            // Возвращаем как FileContentResult - файл будет скачан сразу
            return new FileContentResult(fileBytes, "application/zip")
            {
                FileDownloadName = $"{zipFileName}.zip"
            };
        }
    }
}