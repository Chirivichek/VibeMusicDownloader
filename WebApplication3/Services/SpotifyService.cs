using SpotifyAPI.Web;

namespace WebApplication3.Services
{
    public class SpotifyService
    {
        private SpotifyClient? _spotifyClient; // Removed readonly modifier to allow assignment outside the constructor.
        public readonly string? _clientId;
        public readonly string? _clientSecret;

        public SpotifyService(string? clientId, string? clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        public async Task InitializeAsync(string clientId, string clientSecret)
        {
            try
            {
                var config = SpotifyClientConfig.CreateDefault();
                var request = new ClientCredentialsRequest(clientId, clientSecret);
                var response = await new OAuthClient(config).RequestToken(request);
                _spotifyClient = new SpotifyClient(config.WithToken(response.AccessToken));
            }
            catch (APIException ex)
            {
                // Handle exceptions (e.g., log the error)
                throw new InvalidOperationException("Failed to initialize SpotifyClient.", ex);
            }
        }

        public async Task<List<FullTrack>> SearchTrackAsync(string query)
        {
            if (_spotifyClient == null)
            {
                throw new InvalidOperationException("SpotifyClient is not initialized. Call InitializeAsync first.");
            }

            var result = await _spotifyClient.Search.Item(new SearchRequest(SearchRequest.Types.Track, query));
            var tracks = result.Tracks?.Items ?? new List<FullTrack>();

            foreach (var track in tracks)
            {
                var fullTrack = await _spotifyClient.Tracks.Get(track.Id);
                track.ExternalIds = fullTrack.ExternalIds; // Обновляем с ISRC
            }
            return tracks;
        }

        public async Task<List<FullTrack>> GetPlaylistTrackAsync(string playlistId)
        {
            try
            {
                try
                {
                    var playlist = await _spotifyClient.Playlists.Get(playlistId);
                    if (playlist == null)
                    {
                        Console.WriteLine($"Playlist {playlistId} not found");
                        return new List<FullTrack>();
                    }
                }
                catch (APIException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"Playlist {playlistId} not found: {ex.Message}");
                    return new List<FullTrack>();
                }

                var tracks = new List<FullTrack>();

                // Получаем все треки из плейлиста
                var page = await _spotifyClient.Playlists.GetItems(playlistId);
                while (page != null)
                {
                    foreach (var item in page.Items)
                    {
                        if (item.Track is FullTrack fullTrack)
                        {
                            tracks.Add(fullTrack);
                        }
                    }
                    if (page.Next == null) break;
                    page = await _spotifyClient.Playlists.GetItems(playlistId, new PlaylistGetItemsRequest
                    {
                        Offset = page.Offset + page.Limit
                    });
                }
                return tracks;
            }
            catch (APIException ex)
            {
                Console.WriteLine($"Error getting playlist: {ex.Message}");
                return new List<FullTrack>();
            }
        }
        public async Task<List<FullPlaylist>> SearchPlaylistAsync(string query)
        {
            try
            {
                var search = await _spotifyClient.Search.Item(new SearchRequest(SearchRequest.Types.Playlist, query)
                    {
                    Limit = 50
                });
                return search.Playlists.Items ?? new List<FullPlaylist>();
            }
            catch (APIException ex)
            {
                Console.WriteLine($"Error searching playlists: {ex.Message}");
                return new List<FullPlaylist>();
            }
        }
    }
}
