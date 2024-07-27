using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Data.Entities;

/// <summary>
///     An entity representing the metadata for a group of trickplay tiles.
/// </summary>
public class MediaSegment
{
    /// <summary>
    ///     Gets or sets the id of the media segment.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the id of the associated item.
    /// </summary>
    [JsonIgnore]
    public Guid ItemId { get; set; }

    /// <summary>
    ///     Gets or sets the start of the segment.
    /// </summary>
    public int StartTick { get; set; }

    /// <summary>
    ///     Gets or sets the end of the segment.
    /// </summary>
    public int EndTick { get; set; }

    /// <summary>
    ///     Gets or sets the Type of content this segment defines.
    /// </summary>
    public MediaSegmentType Type { get; set; }
}
