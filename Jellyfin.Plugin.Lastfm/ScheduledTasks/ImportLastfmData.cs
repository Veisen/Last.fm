﻿namespace Jellyfin.Plugin.Lastfm.ScheduledTasks
{
    using Api;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Model.Tasks;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Audio;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Serialization;
    using Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Utils;
    using Microsoft.Extensions.Logging;

    class ImportLastfmData : IScheduledTask
    {
        private readonly IUserManager _userManager;
        private readonly LastfmApiClient _apiClient;
        private readonly IUserDataManager _userDataManager;
        private ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public ImportLastfmData(IHttpClient httpClient, IJsonSerializer jsonSerializer, IUserManager userManager, IUserDataManager userDataManager, ILibraryManager libraryManager, ILoggerFactory loggerFactory)
        {
            _userManager = userManager;
            _userDataManager = userDataManager;
            _libraryManager = libraryManager;
            _logger = loggerFactory.CreateLogger("AutoOrganize");

            _apiClient = new LastfmApiClient(httpClient, jsonSerializer, _logger);
        }

        public string Name
        {
            get { return "Import Last.fm Data"; }
        }

        public string Category
        {
            get { return "Last.fm"; }
        }

        public string Key
        {
            get { return "ImportLastfmData"; }
        }

        public string Description
        {
            get { return "Import play counts and favourite tracks for each user with Last.fm accounted configured"; }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new TaskTriggerInfo[]
            {
                //new WeeklyTrigger { DayOfWeek = DayOfWeek.Sunday, TimeOfDay = TimeSpan.FromHours(3) }
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            //Get all users
            var users = _userManager.Users.Where(u =>
            {
                var user = UserHelpers.GetUser(u);

                return user != null && !String.IsNullOrWhiteSpace(user.SessionKey);
            }).ToList();

            if (users.Count == 0)
            {
                _logger.LogInformation("No users found");
                return;
            }

            Plugin.Syncing = true;

            var usersProcessed = 0;
            var totalUsers = users.Count;

            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progressOffset = ((double)usersProcessed++ / totalUsers);
                var maxProgressForStage = ((double)usersProcessed / totalUsers);


                await SyncDataforUserByArtistBulk(user, progress, cancellationToken, maxProgressForStage, progressOffset);
            }

            Plugin.Syncing = false;
        }


        private async Task SyncDataforUserByArtistBulk(User user, IProgress<double> progress, CancellationToken cancellationToken, double maxProgress, double progressOffset)
        {
            var artists = _libraryManager.GetArtists(new InternalItemsQuery(user))
                .Items
                .Select(i => i.Item1)
                .Cast<MusicArtist>()
                .ToList();

            var lastFmUser = UserHelpers.GetUser(user);

            var totalSongs = 0;
            var matchedSongs = 0;

            //Get loved tracks
            var lovedTracksReponse = await _apiClient.GetLovedTracks(lastFmUser).ConfigureAwait(false);
            var hasLovedTracks = lovedTracksReponse.HasLovedTracks();

            var trackCount = 0;
            //Loop through each artist
            foreach (var artist in artists)
            {
                cancellationToken.ThrowIfCancellationRequested();

                //Get all the tracks by the current artist
                var artistMBid = Helpers.GetMusicBrainzArtistId(artist);

                if (artistMBid == null)
                    continue;

                var allArtistTracks = await GetArtistTracks(lastFmUser, artist, progress, cancellationToken, maxProgress, progressOffset).ConfigureAwait(false);

                //Get the tracks from lastfm for the current artist
                var artistTracks = allArtistTracks.Where(t => t.Artist.MusicBrainzId.Equals(artistMBid));
                trackCount += artistTracks.Count();

                if (artistTracks == null || !artistTracks.Any())
                {
                    _logger.LogInformation("{0} has no tracks in last.fm library for {1}", user.Name, artist.Name);
                    continue;
                }

                var artistTracksList = artistTracks.ToList();

                _logger.LogInformation("Found {0} tracks in last.fm library for {1}", artistTracksList.Count, artist.Name);

                //Loop through each song
                foreach (var song in artist.GetRecursiveChildren().OfType<Audio>())
                {
                    totalSongs++;

                    var matchedSong = Helpers.FindMatchedLastfmSong(artistTracksList, song);

                    if (matchedSong == null)
                        continue;

                    //We have found a match
                    matchedSongs++;

                    _logger.LogDebug("Found match for {0} = {1}", song.Name, matchedSong.Name);

                    var userData = _userDataManager.GetUserData(user, song);

                    //Check if its a favourite track
                    if (hasLovedTracks && lastFmUser.Options.SyncFavourites)
                    {
                        //Use MBID if set otherwise match on song name
                        var favourited = lovedTracksReponse.LovedTracks.Tracks.Any(
                            t => String.IsNullOrWhiteSpace(t.MusicBrainzId)
                                ? StringHelper.IsLike(t.Name, matchedSong.Name)
                                : t.MusicBrainzId.Equals(matchedSong.MusicBrainzId)
                        );

                        userData.IsFavorite = favourited;

                        _logger.LogDebug("{0} Favourite: {1}", song.Name, favourited);
                    }

                    //Update the play count
                    /*if (matchedSong.PlayCount > 0)
                    {
                        userData.Played = true;
                        userData.PlayCount = Math.Max(userData.PlayCount, matchedSong.PlayCount);
                    }
                    else
                    {
                        userData.Played = false;
                        userData.PlayCount = 0;
                        userData.LastPlayedDate = null;
                    }*/

                    _userDataManager.SaveUserData(userData.UserId, song, userData, UserDataSaveReason.UpdateUserRating, cancellationToken);
                }
            }

            //The percentage might not actually be correct but I'm pretty tired and don't want to think about it
           _logger.LogInformation("Finished import Last.fm library for {0}. Local Songs: {1} | Last.fm Songs: {2} | Matched Songs: {3} | {4}% match rate",
                user.Name, totalSongs, trackCount, matchedSongs, Math.Round(((double)matchedSongs / Math.Min(trackCount, totalSongs)) * 100));
        }

        private async Task<List<LastfmArtistTrack>> GetArtistTracks(LastfmUser lastfmUser, MusicArtist artist, IProgress<double> progress, CancellationToken cancellationToken, double maxProgress, double progressOffset) {
            var tracks = new List<LastfmArtistTrack>();
            var page = 1; //Page 0 = 1
            bool moreTracks;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response = await _apiClient.GetArtistTracks(lastfmUser, artist, cancellationToken, page++).ConfigureAwait(false);

                if (response == null || !response.HasTracks())
                    break;

                tracks.AddRange(response.ArtistTracks.Tracks);

                moreTracks = !response.ArtistTracks.Metadata.IsLastPage();
            } while (moreTracks);

            return tracks;
        }

        private async Task<List<LastfmTrack>> GetUsersLibrary(LastfmUser lastfmUser, IProgress<double> progress, CancellationToken cancellationToken, double maxProgress, double progressOffset)
        {
            var tracks = new List<LastfmTrack>();
            var page = 1; //Page 0 = 1
            bool moreTracks;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response = await _apiClient.GetTracks(lastfmUser, cancellationToken, page++).ConfigureAwait(false);

                if (response == null || !response.HasTracks())
                    break;

                tracks.AddRange(response.Tracks.Tracks);

                moreTracks = !response.Tracks.Metadata.IsLastPage();

                //Only report progress in download because it will be 90% of the time taken
                var currentProgress = ((double)response.Tracks.Metadata.Page / response.Tracks.Metadata.TotalPages) * (maxProgress - progressOffset) + progressOffset;

                _logger.LogDebug("Progress: " + currentProgress * 100);

                progress.Report(currentProgress * 100);
            } while (moreTracks);

            return tracks;
        }
    }
}
