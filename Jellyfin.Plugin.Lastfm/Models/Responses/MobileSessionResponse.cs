﻿namespace Jellyfin.Plugin.Lastfm.Models.Responses
{
    using System.Runtime.Serialization;

    [DataContract]
    public class MobileSessionResponse : BaseResponse
    {
        [DataMember(Name="session")]
        public MobileSession Session { get; set; }
    }
}
