using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SpotifyAPI.Web;
using WebApplication3.Services;

namespace WebApplication3.Pages
{
    public class TrackInfo
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Url { get; set; } // ������ �� �������� (��������, YouTube)

        public string? Isrc { get; set; } // ISRC ���
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
        public List<TrackInfo> Results { get; set; } = new();
        public List<PlaylistInfo> PlaylistResult { get; set; } = new();
        public List<TrackInfo> PlaylistTracks { get; set; } = new();
        public List<TrackInfo> FailedTracks { get; set; } = new();
        public string SelectedPlaylistId { get; set; }
        // ����� ��� ������ ������ � ����������
        public async Task OnGetAsync(string query,string playlistUrl, string playlistId) // Fixed typo in method name
        {
            //����� ������
            if (!string.IsNullOrWhiteSpace(query) && string.IsNullOrEmpty(playlistUrl))
            {
                await _spotifyService.InitializeAsync(_spotifyService._clientId!, _spotifyService._clientSecret!); // Ensure the client is initialized
                var tracks = await _spotifyService.SearchTrackAsync(query); // Retrieve the list of FullTrack
                // ��� ��� ��������!!!!!
                var trackInfoTasks = tracks.Select(async track =>
                {
                    var trackInfo = new TrackInfo
                    {
                        Title = track.Name,
                        Artist = string.Join(", ", track.Artists.Select(artist => artist.Name)),
                        Url = track.ExternalUrls["spotify"],
                        Isrc = track.ExternalIds?["isrc"],
                        IsAvailable = true
                    };

                    try
                    {
                        var deezerId = await _musicService.GetDeezerTrackId(track.Name, string.Join(", ", track.Artists.Select(a => a.Name)));
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
                        trackInfo.ErrorMessage = $"������ ������: {ex.Message}";
                    }

                    return trackInfo;
                });
                Results = (await Task.WhenAll(trackInfoTasks)).ToList(); // Convert the array to a List
            }        

            //����� ���������� �� ������
            if (!string.IsNullOrWhiteSpace(playlistUrl))
            {
                var playlistIdFormUrl = ExtractPlaylistIdFormUrl(playlistUrl);
                if (!string.IsNullOrEmpty(playlistIdFormUrl))
                {
                    await LoadPlaylistById(playlistIdFormUrl);
                }
                else
                {
                    TempData["Error"] = "�������� ������ �� �������� Spotify";
                }
            }
            // �������� ������ ��������� (�� ID)
            if (!string.IsNullOrWhiteSpace(playlistId))
            {
                await LoadPlaylistById(playlistId);
            }
            // ��������� ������ ���������
            if (!string.IsNullOrWhiteSpace(playlistId))
            {
                await _spotifyService.InitializeAsync(_spotifyService._clientId!, _spotifyService._clientSecret!);
                var tracks = await _spotifyService.GetPlaylistTrackAsync(playlistId);

                var trackInfoTasks = tracks.Select(async track => new TrackInfo
                {
                    Title = track.Name,
                    Artist = string.Join(", ", track.Artists.Select(artist => artist.Name)),
                    Url = track.ExternalUrls["spotify"],
                    Isrc = track.ExternalIds?["isrc"],
                    DeezerId = await _musicService.GetDeezerTrackId(track.Name, string.Join(", ", track.Artists.Select(a => a.Name))),
                    DeezerUrl = await _musicService.GetDeezerTrackUrl(track.Name, string.Join(", ", track.Artists.Select(a => a.Name)))
                });
                PlaylistTracks = (await Task.WhenAll(trackInfoTasks)).ToList();
                SelectedPlaylistId = playlistId;
            }
        }

        private async Task LoadPlaylistById(string playlistId)
        {
            try
            {
                await _spotifyService.InitializeAsync(_spotifyService._clientId!, _spotifyService._clientSecret!);

                // �������� ���������� � ���������
                var playlist = await _spotifyService.SearchPlaylistAsync(playlistId);
                if(playlist != null)
                {
                    PlaylistResult = playlist.Where(playlist => playlist != null).Select(playlist => new PlaylistInfo
                    {
                            Id = playlist.Id,
                            Name = playlist.Name,
                            Description = playlist.Description,
                            ImageUrl = playlist.Images.FirstOrDefault()?.Url,
                            Owner = playlist.Owner.DisplayName,
                            TrackCount = playlist.Tracks.Total,
                            Url = playlist.ExternalUrls["spotify"]
                       
                    }).Where(playlistInfo => playlistInfo != null).ToList();
                }

                // �������� ����� ���������
                var tracks = await _spotifyService.GetPlaylistTrackAsync(playlistId);
                var trackInfoTasks = tracks.Select(async track =>
                {
                    var trackInfo = new TrackInfo
                    {
                        Title = track.Name,
                        Artist = string.Join(", ", track.Artists.Select(artist => artist.Name)),
                        Url = track.ExternalUrls["spotify"],
                        Isrc = track.ExternalIds?["isrc"],
                        IsAvailable = true
                    };
                    try
                    {
                        // �������� ����� Deezer ID
                        var deezerId = await _musicService.GetDeezerTrackId(track.Name, string.Join(", ", track.Artists.Select(a => a.Name)));
                        if (deezerId != null)
                        {
                            trackInfo.DeezerId = deezerId;
                            trackInfo.DeezerUrl = $"https://www.deezer.com/track/{deezerId}";
                        }
                        else
                        {
                            trackInfo.IsAvailable = false;
                            trackInfo.ErrorMessage = "���� �� ������ �� Deezer";
                        }
                    }
                    catch (Exception ex)
                    {
                        trackInfo.IsAvailable = false;
                        trackInfo.ErrorMessage = $"������ ������: {ex.Message}";
                    }

                    return trackInfo;
                });

                PlaylistTracks = (await Task.WhenAll(trackInfoTasks)).ToList();
                SelectedPlaylistId = playlistId;
                // ��������� ��������� �����
                FailedTracks = PlaylistTracks.Where(t => !t.IsAvailable).ToList();
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ �������� ���������: {ex.Message}";
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

                FailedTracks = failedTracks;
                TempData["FailedTracksCount"] = failedTracks.Count.ToString();

                return result;
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"������ ��� ���������� ���������: {ex.Message}";
                return RedirectToPage();
            }
        }
    }
}
