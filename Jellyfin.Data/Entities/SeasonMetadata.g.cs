//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
//
//     Produced by Entity Framework Visual Editor
//     https://github.com/msawczyn/EFDesigner
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Jellyfin.Data
{
   public partial class SeasonMetadata: global::Jellyfin.Data.Metadata
   {
      partial void Init();

      /// <summary>
      /// Default constructor. Protected due to required properties, but present because EF needs it.
      /// </summary>
      protected SeasonMetadata(): base()
      {
         Init();
      }

      /// <summary>
      /// Replaces default constructor, since it's protected. Caller assumes responsibility for setting all required values before saving.
      /// </summary>
      public static SeasonMetadata CreateSeasonMetadataUnsafe()
      {
         return new SeasonMetadata();
      }

      /// <summary>
      /// Public constructor with required data
      /// </summary>
      /// <param name="title">The title or name of the object</param>
      /// <param name="language">ISO-639-3 3-character language codes</param>
      /// <param name="_season0"></param>
      public SeasonMetadata(string title, string language, DateTime dateadded, DateTime lastmodified, global::Jellyfin.Data.Season _season0)
      {
         if (string.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
         this.Title = title;

         if (string.IsNullOrEmpty(language)) throw new ArgumentNullException(nameof(language));
         this.Language = language;

         if (_season0 == null) throw new ArgumentNullException(nameof(_season0));
         _season0.SeasonMetadata.Add(this);


         Init();
      }

      /// <summary>
      /// Static create function (for use in LINQ queries, etc.)
      /// </summary>
      /// <param name="title">The title or name of the object</param>
      /// <param name="language">ISO-639-3 3-character language codes</param>
      /// <param name="_season0"></param>
      public static SeasonMetadata Create(string title, string language, DateTime dateadded, DateTime lastmodified, global::Jellyfin.Data.Season _season0)
      {
         return new SeasonMetadata(title, language, dateadded, lastmodified, _season0);
      }

      /*************************************************************************
       * Properties
       *************************************************************************/

      /// <summary>
      /// Backing field for Outline
      /// </summary>
      protected string _Outline;
      /// <summary>
      /// When provided in a partial class, allows value of Outline to be changed before setting.
      /// </summary>
      partial void SetOutline(string oldValue, ref string newValue);
      /// <summary>
      /// When provided in a partial class, allows value of Outline to be changed before returning.
      /// </summary>
      partial void GetOutline(ref string result);

      /// <summary>
      /// Max length = 1024
      /// </summary>
      [MaxLength(1024)]
      [StringLength(1024)]
      public string Outline
      {
         get
         {
            string value = _Outline;
            GetOutline(ref value);
            return (_Outline = value);
         }
         set
         {
            string oldValue = _Outline;
            SetOutline(oldValue, ref value);
            if (oldValue != value)
            {
               _Outline = value;
            }
         }
      }

      /// <summary>
      /// Concurrency token
      /// </summary>
      [Timestamp]
      public Byte[] Timestamp { get; set; }

      /*************************************************************************
       * Navigation properties
       *************************************************************************/

   }
}

