﻿using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System.Linq;

namespace Jellyfin.Plugin.Lastfm.Providers
{
    public static class Extensions
    {
        public static string GetAlbumArtist(this AlbumInfo info)
        {
            var id = info.AlbumArtists.FirstOrDefault();

            if (string.IsNullOrEmpty(id))
            {
                return info.SongInfos.SelectMany(i => i.AlbumArtists)
                    .FirstOrDefault(i => !string.IsNullOrEmpty(i));
            }

            return id;
        }

        public static string GetReleaseGroupId(this AlbumInfo info)
        {
            var id = info.GetProviderId(MetadataProviders.MusicBrainzReleaseGroup);

            if (string.IsNullOrEmpty(id))
            {
                return info.SongInfos.Select(i => i.GetProviderId(MetadataProviders.MusicBrainzReleaseGroup))
                    .FirstOrDefault(i => !string.IsNullOrEmpty(i));
            }

            return id;
        }

        public static string GetReleaseId(this AlbumInfo info)
        {
            var id = info.GetProviderId(MetadataProviders.MusicBrainzAlbum);

            if (string.IsNullOrEmpty(id))
            {
                return info.SongInfos.Select(i => i.GetProviderId(MetadataProviders.MusicBrainzAlbum))
                    .FirstOrDefault(i => !string.IsNullOrEmpty(i));
            }

            return id;
        }

        public static string GetMusicBrainzArtistId(this AlbumInfo info)
        {
            string id;
            info.ProviderIds.TryGetValue(MetadataProviders.MusicBrainzAlbumArtist.ToString(), out id);
            
            if (string.IsNullOrEmpty(id))
            {
                return info.SongInfos.Select(i => i.GetProviderId(MetadataProviders.MusicBrainzAlbumArtist))
                    .FirstOrDefault(i => !string.IsNullOrEmpty(i));
            }

            return id;
        }

        public static string GetMusicBrainzArtistId(this ArtistInfo info)
        {
            string id;
            info.ProviderIds.TryGetValue(MetadataProviders.MusicBrainzArtist.ToString(), out id);

            return id;
        }
    }
}
