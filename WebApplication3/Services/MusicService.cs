using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders.Physical;
using Newtonsoft.Json.Linq;
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
        // Метод для получения ID трека в Deezer
        public async Task<string?> GetDeezerTrackId(string title, string artist)
        {
            try
            {
                // Кодируем параметры для URL
                var encodedArtist = Uri.EscapeDataString(artist);
                var encodedTitle = Uri.EscapeDataString(title);
                var searchUrl = $"https://api.deezer.com/search?q=artist:\"{encodedArtist}\" track:\"{encodedTitle}\"&limit=1";

                var response = await _httpClient.GetStringAsync(searchUrl);
                var json = JObject.Parse(response);

                // Проверяем наличие данных
                if (json["data"] == null)
                {
                    return null;
                }

                var dataArray = json["data"] as JArray;
                if (dataArray == null || dataArray.Count == 0)
                {
                    Console.WriteLine($"No tracks found on Deezer for: {artist} - {title}");
                    return null;
                }


                var firstTrack = dataArray[0];
                if (firstTrack == null || firstTrack["id"] == null)
                {
                    Console.WriteLine($"No valid track ID found in Deezer response for: {artist} - {title}");
                    return null;
                }
                var trackId = firstTrack["id"]?.ToString();
                if (string.IsNullOrEmpty(trackId))
                {
                    Console.WriteLine($"Empty track ID in Deezer response for: {artist} - {title}");
                    return null;
                }
                Console.WriteLine($"Found Deezer track ID: {trackId} for {artist} - {title}");
                return trackId;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP error getting Deezer track ID for {artist} - {title}: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parsing error for {artist} - {title}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error getting Deezer track ID for {artist} - {title}: {ex.Message}");
                return null;
            }
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
                        Url = track.ExternalUrls["spotify"],
                        Isrc = track.ExternalIds["isrc"],
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