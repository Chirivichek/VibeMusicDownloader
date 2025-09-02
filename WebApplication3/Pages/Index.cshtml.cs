using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SpotifyAPI.Web;
using WebApplication3.Services;

namespace WebApplication3.Pages
{
    public class DownloadProgress
    {
        public string PlaylistId { get; set; }
        public int TotalTracks { get; set; }
        public int DownloadedTracks { get; set; }
        public int FailedTracks { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime StartTime { get; set; }
        public string CurrentTrack { get; set; }
    }
    public class DeezerPlaylistInfo
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? Owner { get; set; }
        public int TrackCount { get; set; }
        public string? Url { get; set; }
        public int Duration { get; set; } // � ��������
        public bool IsPublic { get; set; }
    }

    public class DeezerTrack
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? DeezerId { get; set; }
        public string? DeezerUrl { get; set; }
    }
    public class TrackInfo
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? DeezerId { get; set; } // Deezer ID
        public string? DeezerUrl { get; set; } // ������ �� Deezer
        public bool IsAvailable { get; set; }// �������� �� ���� ��� ����������
        public string? ErrorMessage { get; set; } // ��������� �� ������, ���� ���� ����������
    }
    public class PlaylistInfo
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? Owner { get; set; }
        public int? TrackCount { get; set; }
        public string? Url { get; set; }
        public bool IsDeezerPlaylist { get; set; }
    }
    public class IndexModel : PageModel
    {
        private readonly SpotifyService _spotifyService;
        private readonly MusicService _musicService;

        public IndexModel(SpotifyService spotifyService, MusicService musicService)
        {
            _spotifyService = spotifyService;
            _musicService = musicService;
        }
        // �������� ��� ������
        public bool IsDeezerPlaylist { get; set; }
        public List<TrackInfo> Results { get; set; } = new();
        public List<PlaylistInfo> PlaylistResult { get; set; } = new();
        public List<TrackInfo> PlaylistTracks { get; set; } = new();
        public List<TrackInfo> FailedTracks { get; set; } = new();
        public string SelectedPlaylistId { get; set; }
        [BindProperty]
        public DownloadProgress Progress { get; set; }

        public static Dictionary<string, DownloadProgress> ActiveDownloads = new Dictionary<string, DownloadProgress>();

        public async Task OnGetAsync(string query, string playlistUrl,string deezerPlaylistUrl, string playlistId)
        {
            // ������ ����� � DEEZER
            if (!string.IsNullOrWhiteSpace(query) && string.IsNullOrEmpty(playlistUrl) && string.IsNullOrEmpty(deezerPlaylistUrl) && string.IsNullOrEmpty(playlistId))
            {
                Results = await _musicService.SearchDeezerAsync(query);
                return;
            }
            // �������� ��������� SPOTIFY �� ������
            if (!string.IsNullOrWhiteSpace(playlistUrl))
            {
                var playlistIdFromUrl = ExtractPlaylistIdFormUrl(playlistUrl);
                if (!string.IsNullOrEmpty(playlistIdFromUrl))
                {
                    await LoadPlaylistById(playlistIdFromUrl);
                }
            }
            // �������� ��������� DEEZER �� ������
            if (!string.IsNullOrWhiteSpace(deezerPlaylistUrl))
            {
                var deezerPlaylistId = ExtractDeezerPlaylistIdFormUrl(deezerPlaylistUrl);
                if (!string.IsNullOrEmpty(deezerPlaylistId))
                {
                    await LoadDeezerPlaylistById(deezerPlaylistId);
                }
            }
            // �������� ����������� ��������� �� ID
            if (!string.IsNullOrWhiteSpace(playlistId))
            {
                // ���������� ��� ��������� �� ������� ID ��� ������ ���������
                if (IsDeezerPlaylistId(playlistId))
                {
                    await LoadDeezerPlaylistById(playlistId);
                }
                else
                {
                    await LoadSpotifyPlaylistById(playlistId);
                }
            }
            //// ������ ����� � DEEZER
            //if (!string.IsNullOrWhiteSpace(query) && string.IsNullOrEmpty(playlistUrl))
            //{
            //    Results = await _musicService.SearchDeezerAsync(query);
            //    return;
            //}

            //// �������� ��������� SPOTIFY �� ������
            //if (!string.IsNullOrWhiteSpace(playlistUrl))
            //{
            //    var playlistIdFromUrl = ExtractPlaylistIdFormUrl(playlistUrl);
            //    if (!string.IsNullOrEmpty(playlistIdFromUrl))
            //    {
            //        await LoadPlaylistById(playlistIdFromUrl);
            //    }
            //}

            //// �������� ��������� DEEZER �� ������
            //if (!string.IsNullOrWhiteSpace(deezerPlaylistUrl))
            //{
            //    var deezerPlaylistId = ExtractDeezerPlaylistIdFormUrl(deezerPlaylistUrl);
            //    if (!string.IsNullOrEmpty(deezerPlaylistId))
            //    {
            //        await LoadDeezerPlaylistById(deezerPlaylistId);
            //    }
            //}

            //if (!string.IsNullOrWhiteSpace(playlistId))
            //{
            //    await LoadPlaylistById(playlistId);
            //    SelectedPlaylistId = playlistId;
            //}
        }
        private bool IsDeezerPlaylistId(string playlistId)
        {
            // Deezer ID ������ ������� �����, Spotify - 22 �������
            return playlistId.Length > 22 || long.TryParse(playlistId, out _);
        }
        private async Task LoadPlaylistById(string playlistId)
        {
            try
            {
                await _spotifyService.InitializeAsync(_spotifyService._clientId!, _spotifyService._clientSecret!);

                // �������� ���������� � ���������
                var playlist = await _spotifyService.GetPlaylistAsync(playlistId);
                if (playlist == null)
                {
                    TempData["Error"] = "�������� �� ������";
                    return;
                }

                // 2. ��������� ���������� � ���������
                PlaylistResult = new List<PlaylistInfo>
        {
                      new PlaylistInfo
                      {
                          Id = playlist.Id,
                          Name = playlist.Name,
                          Description = playlist.Description,
                          ImageUrl = playlist.Images.FirstOrDefault()?.Url,
                          Owner = playlist.Owner.DisplayName,
                          TrackCount = playlist.Tracks.Total,
                          Url = playlist.ExternalUrls["spotify"]
                      }
                 };

                // 3. �������� ����� �� Spotify
                var spotifyTracks = await _spotifyService.GetPlaylistTrackAsync(playlistId);

                // 4. ���� ������ ���� ����� � Deezer!
                var trackInfos = new List<TrackInfo>();

                foreach (var spotifyTrack in spotifyTracks)
                {
                    var trackInfo = new TrackInfo
                    {
                        Title = spotifyTrack.Name,
                        Artist = string.Join(", ", spotifyTrack.Artists.Select(artist => artist.Name)),
                        IsAvailable = true
                    };

                    try
                    {
                        // ���� ����� � DEEZER �� �������� � �������
                        var deezerId = await _musicService.GetDeezerTrackId(
                            spotifyTrack.Name,
                            string.Join(", ", spotifyTrack.Artists.Select(a => a.Name)),
                            spotifyTrack.Album.Name,
                            spotifyTrack.DurationMs
                        );

                        if (deezerId != null)
                        {
                            trackInfo.DeezerId = deezerId;
                            trackInfo.DeezerUrl = $"https://www.deezer.com/track/{deezerId}";
                        }
                        else
                        {
                            trackInfo.IsAvailable = false;
                            trackInfo.ErrorMessage = "���� �� ������ � Deezer";
                        }
                    }
                    catch (Exception ex)
                    {
                        trackInfo.IsAvailable = false;
                        trackInfo.ErrorMessage = $"������: {ex.Message}";
                    }

                    trackInfos.Add(trackInfo);

                    // ��������� ����� ����� ��������� � Deezer API
                    await Task.Delay(100);
                }
                PlaylistTracks = trackInfos;
                FailedTracks = trackInfos.Where(t => !t.IsAvailable).ToList();
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ ��� �������� ���������: {ex.Message}";
            }
        }

        private string ExtractPlaylistIdFormUrl(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                    return null;

                url = url.Trim();

                if (url.Contains("open.spotify.com/playlist/"))
                {
                    var uri = new Uri(url);
                    var segments = uri.AbsolutePath.Split('/');
                    var playlistId = segments.LastOrDefault();
                    return playlistId?.Split('?')[0];
                }
                else if (url.Contains("spotify:playlist:"))
                {
                    return url.Split(':').LastOrDefault();
                }
                else if (url.Length == 22)
                {
                    return url;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
        public async Task<IActionResult> OnGetDownload(string deezerUrl, string format = "mp3")
        {
            try
            {
                var result = await _musicService.DownloadTrackWithSaveDialog(deezerUrl,format);
                return result;
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ ��� ����������: {ex.Message}";
                return RedirectToPage();
            }
        }
        public async Task<IActionResult> OnGetDownloadPlaylist(string playlistId, string format = "mp3")
        {
            try
            {
                var (result, failedTracks) = await _musicService.DownloadPlaylistAsync(playlistId, format);

                // �������� ������ ���������, ���� ������ �������� �� �� �������� ����� ����������
                FailedTracks = failedTracks;

                // ������ ���������� �������� ��������� (������ ��� RedirectResult �� /music/xxx.zip)
                return result;
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ ��� ���������� ���������: {ex.Message}";
                return RedirectToPage();
            }
        }
        private string ExtractDeezerPlaylistIdFormUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return null;

                url = url.Trim();

                // ������ �������� ID
                if (long.TryParse(url, out _))
                    return url;

                // Deezer URI scheme
                if (url.StartsWith("deezer:playlist:"))
                    return url.Split(':').LastOrDefault();

                // ���������� ��������� ��� ������ ID ���������
                var regex = new Regex(@"(?:playlist/|album/|track/)(\d+)", RegexOptions.IgnoreCase);
                var match = regex.Match(url);

                if (match.Success)
                    return match.Groups[1].Value;

                // ����� ������ ��������� ID � URL (��������� ������)
                var digitRegex = new Regex(@"\d+");
                var digitsMatch = digitRegex.Match(url);
                if (digitsMatch.Success && digitsMatch.Value.Length >= 6) // ID ������ �������
                    return digitsMatch.Value;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task LoadSpotifyPlaylistById(string playlistId)
        {
            try
            {
                IsDeezerPlaylist = false;
                await _spotifyService.InitializeAsync(_spotifyService._clientId!, _spotifyService._clientSecret!);

                var playlist = await _spotifyService.GetPlaylistAsync(playlistId);
                if (playlist == null)
                {
                    TempData["Error"] = "�������� Spotify �� ������";
                    return;
                }

                PlaylistResult = new List<PlaylistInfo>
                {
                    new PlaylistInfo
                    {
                        Id = playlist.Id,
                        Name = playlist.Name,
                        Description = playlist.Description,
                        ImageUrl = playlist.Images.FirstOrDefault()?.Url,
                        Owner = playlist.Owner.DisplayName,
                        TrackCount = playlist.Tracks.Total,
                        Url = playlist.ExternalUrls["spotify"],
                        IsDeezerPlaylist = false
                    }
                };

                var spotifyTracks = await _spotifyService.GetPlaylistTrackAsync(playlistId);
                var trackInfos = new List<TrackInfo>();

                foreach (var spotifyTrack in spotifyTracks)
                {
                    var trackInfo = new TrackInfo
                    {
                        Title = spotifyTrack.Name,
                        Artist = string.Join(", ", spotifyTrack.Artists.Select(artist => artist.Name)),
                        IsAvailable = true
                    };

                    try
                    {
                        var deezerId = await _musicService.GetDeezerTrackId(
                            spotifyTrack.Name,
                            string.Join(", ", spotifyTrack.Artists.Select(a => a.Name)),
                            spotifyTrack.Album.Name,
                            spotifyTrack.DurationMs
                        );

                        if (deezerId != null)
                        {
                            trackInfo.DeezerId = deezerId;
                            trackInfo.DeezerUrl = $"https://www.deezer.com/track/{deezerId}";
                        }
                        else
                        {
                            trackInfo.IsAvailable = false;
                            trackInfo.ErrorMessage = "���� �� ������ � Deezer";
                        }
                    }
                    catch (Exception ex)
                    {
                        trackInfo.IsAvailable = false;
                        trackInfo.ErrorMessage = $"������: {ex.Message}";
                    }

                    trackInfos.Add(trackInfo);
                    await Task.Delay(100);
                }

                PlaylistTracks = trackInfos;
                FailedTracks = trackInfos.Where(t => !t.IsAvailable).ToList();
                SelectedPlaylistId = playlistId;
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ ��� �������� ��������� Spotify: {ex.Message}";
            }
        }
        private async Task LoadDeezerPlaylistById(string deezerPlaylistId)
        {
            try
            {
                IsDeezerPlaylist = true;
                // �������� ���������� � ��������� �� Deezer
                var playlistInfo = await _musicService.GetDeezerPlaylistInfo(deezerPlaylistId);
                if (playlistInfo == null)
                {
                    TempData["Error"] = "Deezer �������� �� ������";
                    return;
                }

                // ��������� ���������� � ���������
                PlaylistResult = new List<PlaylistInfo>
        {
            new PlaylistInfo
            {
                Id = deezerPlaylistId,
                Name = playlistInfo.Title,
                Description = playlistInfo.Description,
                ImageUrl = playlistInfo.ImageUrl,
                Owner = playlistInfo.Owner,
                TrackCount = playlistInfo.TrackCount,
                Url = playlistInfo.Url
            }
        };

                // �������� ����� �� Deezer ���������
                var deezerTracks = await _musicService.GetDeezerPlaylistTracks(deezerPlaylistId);

                var trackInfos = deezerTracks.Select(track => new TrackInfo
                {
                    Title = track.Title,
                    Artist = track.Artist,
                    DeezerId = track.DeezerId,
                    DeezerUrl = track.DeezerUrl,
                    IsAvailable = true
                }).ToList();

                PlaylistTracks = trackInfos;
                SelectedPlaylistId = deezerPlaylistId;
                FailedTracks = new List<TrackInfo>(); // ��� ����� ��������, ��� ��� ��� Deezer ��������
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ �������� Deezer ���������: {ex.Message}";
            }
        }
        public async Task<IActionResult> OnGetDownloadDeezerPlaylist(string playlistId, string format = "mp3")
        {
            try
            {
                var progress = new DownloadProgress
                {
                    PlaylistId = playlistId,
                    StartTime = DateTime.Now
                };

                ActiveDownloads[playlistId] = progress;

                var (result, failedTracks) = await _musicService.DownloadDeezerPlaylistAsync(playlistId, format, (p) => { ActiveDownloads[playlistId] = p; });

                // ��������� ���������� � ��������� ������
                FailedTracks = failedTracks;
                TempData["FailedTracksCount"] = failedTracks.Count.ToString();

                // ������� �� �������� �������� ����� ����������
                ActiveDownloads.Remove(playlistId);

                return result;
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ ��� ���������� Deezer ���������: {ex.Message}";
                return RedirectToPage();
            }
        }
        // ����� ����� ��� �������� ���������
        public async Task<IActionResult> OnGetCheckProgress(string playlistId)
        {
            if (ActiveDownloads.TryGetValue(playlistId, out var progress))
            {
                return new JsonResult(progress);
            }
            return new JsonResult(new { error = "Download not found" });
        }
    }
}
