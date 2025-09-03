using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders.Physical;
using Newtonsoft.Json.Linq;
using RussianTransliteration;
using WebApplication3.Pages;
using System.Collections.Concurrent;
using System.Threading;
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
                    Url = json["link"]?.ToString(),
                    Duration = json["duration"]?.ToObject<int>() ?? 0,
                    IsPublic = json["public"]?.ToObject<bool>() ?? true
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
                // Получаем информацию о плейлисте чтобы узнать общее количество треков
                var playlistInfo = await GetDeezerPlaylistInfo(playlistId);
                var totalTracks = playlistInfo?.TrackCount ?? 0;

                // Deezer API ограничивает 2000 треков для бесплатного использования
                var maxTracks = Math.Min(totalTracks, 2000);
                const int limit = 100;


                for (int index = 0; index < maxTracks; index += limit)
                {
                    var tracksUrl = $"https://api.deezer.com/playlist/{playlistId}/tracks?index={index}&limit={limit}";
                    var response = await _httpClient.GetStringAsync(tracksUrl);
                    var json = JObject.Parse(response);

                    if (json["data"] == null || !json["data"].Any())
                        break;

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

                    if (tracks.Count >= maxTracks)
                        break;

                    await Task.Delay(100);
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
        public async Task<(IActionResult Result, List<TrackInfo> FailedTracks)> DownloadDeezerPlaylistAsync(string deezerPlaylistId, string format = "mp3", Action<DownloadProgress> progressCallback = null, HttpContext httpContext = null)
        {
            var failedTracks = new List<TrackInfo>();

            try
            {
                var deezerTracks = await GetDeezerPlaylistTracks(deezerPlaylistId);

                if (deezerTracks.Count == 0)
                    throw new Exception("No tracks found in the Deezer playlist.");

                var progress = new DownloadProgress
                {
                    PlaylistId = deezerPlaylistId,
                    TotalTracks = deezerTracks.Count,
                    StartTime = DateTime.Now,
                    CurrentTrack = "Подготовка к потоковой загрузке..."
                };

                progressCallback?.Invoke(progress);

                // Если есть HTTP контекст, используем потоковую отдачу
                if (httpContext != null)
                {
                    var result = await StreamZipToBrowser(httpContext, deezerTracks, deezerPlaylistId, format, progress, progressCallback, failedTracks);
                    return (result, failedTracks);
                }

                // Старый метод как fallback
                return await DownloadWithTempDirectory(deezerPlaylistId, format, progressCallback, deezerTracks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DownloadDeezerPlaylistAsync: {ex.Message}");
                throw new Exception($"Error downloading Deezer playlist: {ex.Message}");
            }
        }

        private async Task<IActionResult> StreamZipToBrowser(
             HttpContext httpContext,
             List<DeezerTrack> tracks,
             string playlistId,
             string format,
             DownloadProgress progress,
             Action<DownloadProgress> progressCallback,
             List<TrackInfo> failedTracks)
        { 
            var response = httpContext.Response;

            // Устанавливаем заголовки для потоковой отдачи ZIP
            response.Headers.Append("Content-Type", "application/zip");
            response.Headers.Append("Content-Disposition", $"attachment; filename=\"{playlistId}.zip\"");

            var downloadedCount = 0;
            var semaphore = new SemaphoreSlim(2); // Ограничение одновременных загрузок

            // Создаем временный файл для ZIP
            var tempZipPath = Path.GetTempFileName();

            try
            {
                using (var fileStream = new FileStream(tempZipPath, FileMode.Create))
                using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create, true))
                {
                    var downloadTracks = tracks.Select(async deezerTrack =>
                    {
                        await semaphore.WaitAsync();

                        try
                        {
                            progress.CurrentTrack = $"{deezerTrack.Artist} - {deezerTrack.Title}";
                            progressCallback?.Invoke(progress);

                            if (!string.IsNullOrEmpty(progress.CurrentTrack))
                            {
                                var tempFile = await DownloadTrackToTempFile(deezerTrack.DeezerUrl, format);

                                if (tempFile != null && File.Exists(tempFile))
                                {
                                    var entryName = SanitizeFileName($"{deezerTrack.Artist} - {deezerTrack.Title}.{format}");
                                    var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Fastest);

                                    using var entryStream = entry.Open();
                                    using (var fileStreamRead = File.OpenRead(tempFile))
                                    {
                                        await fileStreamRead.CopyToAsync(entryStream);
                                    }
                                    File.Delete(tempFile); // Удаляем временный файл
                                    Interlocked.Increment(ref downloadedCount);
                                }
                                else
                                {
                                    failedTracks.Add(new TrackInfo
                                    {
                                        Title = deezerTrack.Title,
                                        Artist = deezerTrack.Artist,
                                        DeezerId = deezerTrack.DeezerId,
                                        DeezerUrl = deezerTrack.DeezerUrl,
                                        IsAvailable = false,
                                        ErrorMessage = "Ошибка скачивания"
                                    });
                                }
                            }
                            progress.DownloadedTracks = downloadedCount;
                            progress.FailedTracks = failedTracks.Count;
                            progressCallback?.Invoke(progress);

                            await Task.Delay(1000); // Пауза между скачиваниями
                        }
                        catch (Exception ex)
                        {
                            failedTracks.Add(new TrackInfo
                            {
                                Title = deezerTrack.Title,
                                Artist = deezerTrack.Artist,
                                ErrorMessage = ex.Message
                            });
                            progress.FailedTracks = failedTracks.Count;
                            progressCallback?.Invoke(progress);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToArray();

                    await Task.WhenAll(downloadTracks);
                }

                // Отправляем готовый ZIP
                var fileBytes = await File.ReadAllBytesAsync(tempZipPath);

                progress.IsCompleted = true;
                progressCallback?.Invoke(progress);

                return new FileContentResult(fileBytes, "application/zip")
                {
                    FileDownloadName = $"{playlistId}.zip"
                };
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { }
                }
            }
        }
        private async Task<string> DownloadTrackToTempFile(string deezerUrl, string format)
        {
            try
            {
                string pythonPath = @"C:\Users\pavlo\AppData\Local\Programs\Python\Python39\python.exe";
                string bitrate = format.ToLower() == "flac" ? "9" : "3";

                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $" -m deemix -b {bitrate} -p \"{tempDir}\" \"{deezerUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                await process.WaitForExitAsync();

                // Ищем скачанный файл
                var extension = format.ToLower() == "flac" ? ".flac" : ".mp3";
                var downloadedFiles = Directory.GetFiles(tempDir, $"*{extension}")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                if (downloadedFiles.Count > 0)
                {
                    var tempFile = Path.GetTempFileName() + extension;
                    File.Move(downloadedFiles[0], tempFile, true);
                    Directory.Delete(tempDir, true);
                    return tempFile;
                }

                Directory.Delete(tempDir, true);
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return fileName;

            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries))
                .Replace(" ", "_")
                .Trim();
        }

        private async Task<(IActionResult Result, List<TrackInfo> FailedTracks)> DownloadWithTempDirectory(
    string deezerPlaylistId, string format, Action<DownloadProgress> progressCallback, List<DeezerTrack> deezerTracks)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"deezer_playlist_{deezerPlaylistId}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var failedTracks = new List<TrackInfo>();
            var downloadedCount = 0;
            var semaphore = new SemaphoreSlim(3); // Ограничиваем одновременные загрузки

            try
            {
                var progress = new DownloadProgress
                {
                    PlaylistId = deezerPlaylistId,
                    TotalTracks = deezerTracks.Count,
                    StartTime = DateTime.Now,
                    CurrentTrack = "Начинаем загрузку..."
                };

                progressCallback?.Invoke(progress);

                // Создаем задачи для скачивания каждого трека
                var downloadTasks = deezerTracks.Select(async (track, index) =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // Обновляем прогресс
                        progress.CurrentTrack = $"{track.Artist} - {track.Title}";
                        progressCallback?.Invoke(progress);

                        if (!string.IsNullOrEmpty(track.DeezerUrl))
                        {
                            var success = await DownloadSingleTrack(track.DeezerUrl, tempDirectory, format);

                            if (success)
                            {
                                Interlocked.Increment(ref downloadedCount);
                                progress.DownloadedTracks = downloadedCount;
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
                                lock (failedTracks)
                                {
                                    failedTracks.Add(trackInfo);
                                }
                                progress.FailedTracks = failedTracks.Count;
                            }
                            progressCallback?.Invoke(progress);
                        }

                        // Пауза между загрузками
                        await Task.Delay(1200);
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
                        lock (failedTracks)
                        {
                            failedTracks.Add(trackInfo);
                        }
                        progress.FailedTracks = failedTracks.Count;
                        progressCallback?.Invoke(progress);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                // Ожидаем завершения всех задач
                await Task.WhenAll(downloadTasks);

                progress.IsCompleted = true;
                progressCallback?.Invoke(progress);

                if (downloadedCount == 0)
                    throw new Exception("No tracks were downloaded from the Deezer playlist.");

                // Создаем ZIP архив
                var result = await CreateZipInMusicDirectory(tempDirectory, $"deezer_playlist_{deezerPlaylistId}");
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
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete temp directory: {ex.Message}");
                }
            }
        }
        public async Task<bool> DownloadSingleTrack(string deezerUrl, string directoryPath, string format = "mp3")
        {
            string pythonPath = @"C:\Users\pavlo\AppData\Local\Programs\Python\Python39\python.exe";
            string bitrate = format.ToLower() == "flac" ? "9" : "3";

            try
            {
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

                // Проверяем, что файл действительно скачался
                var extension = format.ToLower() == "flac" ? ".flac" : ".mp3";
                var files = Directory.GetFiles(directoryPath, $"*{extension}")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                return files.Count > 0 && process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DownloadSingleTrack: {ex.Message}");
                return false;
            }
        }

        private async Task<IActionResult> CreateZipInMusicDirectory(string sourceDirectory, string zipFileName)
        {
            // Используем временный файл для ZIP вместо memory stream
            var tempZipPath = Path.GetTempFileName();

            try
            {
                // Создаем ZIP архив на диске
                using (var fileStream = new FileStream(tempZipPath, FileMode.Create))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, true))
                {
                    var files = Directory.GetFiles(sourceDirectory);

                    foreach (var filePath in files)
                    {
                        var entryName = Path.GetFileName(filePath);
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

                        using var entryStream = entry.Open();
                        using var fileStreamRead = File.OpenRead(filePath);
                        await fileStreamRead.CopyToAsync(entryStream);
                    }
                }

                // Читаем готовый файл и возвращаем как FileContentResult
                var fileBytes = await File.ReadAllBytesAsync(tempZipPath);

                return new FileContentResult(fileBytes, "application/zip")
                {
                    FileDownloadName = $"{zipFileName}.zip"
                };
            }
            finally
            {
                // Удаляем временный ZIP файл
                if (File.Exists(tempZipPath))
                {
                    try
                    {
                        File.Delete(tempZipPath);
                    }
                    catch
                    {
                        // Игнорируем ошибки удаления временного файла
                    }
                }
            }
        }
    }
}