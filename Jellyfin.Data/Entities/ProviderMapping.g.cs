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
   public partial class ProviderMapping
   {
      partial void Init();

      /// <summary>
      /// Default constructor. Protected due to required properties, but present because EF needs it.
      /// </summary>
      protected ProviderMapping()
      {
         Init();
      }

      /// <summary>
      /// Replaces default constructor, since it's protected. Caller assumes responsibility for setting all required values before saving.
      /// </summary>
      public static ProviderMapping CreateProviderMappingUnsafe()
      {
         return new ProviderMapping();
      }

      /// <summary>
      /// Public constructor with required data
      /// </summary>
      /// <param name="providername"></param>
      /// <param name="providersecrets"></param>
      /// <param name="providerdata"></param>
      /// <param name="_user0"></param>
      /// <param name="_group1"></param>
      public ProviderMapping(string providername, string providersecrets, string providerdata, global::Jellyfin.Data.User _user0, global::Jellyfin.Data.Group _group1)
      {
         if (string.IsNullOrEmpty(providername)) throw new ArgumentNullException(nameof(providername));
         this.ProviderName = providername;

         if (string.IsNullOrEmpty(providersecrets)) throw new ArgumentNullException(nameof(providersecrets));
         this.ProviderSecrets = providersecrets;

         if (string.IsNullOrEmpty(providerdata)) throw new ArgumentNullException(nameof(providerdata));
         this.ProviderData = providerdata;

         if (_user0 == null) throw new ArgumentNullException(nameof(_user0));
         _user0.ProviderMappings.Add(this);

         if (_group1 == null) throw new ArgumentNullException(nameof(_group1));
         _group1.ProviderMappings.Add(this);


         Init();
      }

      /// <summary>
      /// Static create function (for use in LINQ queries, etc.)
      /// </summary>
      /// <param name="providername"></param>
      /// <param name="providersecrets"></param>
      /// <param name="providerdata"></param>
      /// <param name="_user0"></param>
      /// <param name="_group1"></param>
      public static ProviderMapping Create(string providername, string providersecrets, string providerdata, global::Jellyfin.Data.User _user0, global::Jellyfin.Data.Group _group1)
      {
         return new ProviderMapping(providername, providersecrets, providerdata, _user0, _group1);
      }

      /*************************************************************************
       * Properties
       *************************************************************************/

      /// <summary>
      /// Identity, Indexed, Required
      /// </summary>
      [Key]
      [Required]
      public Guid Id { get; protected set; }

      /// <summary>
      /// Required, Max length = 255
      /// </summary>
      [Required]
      [MaxLength(255)]
      [StringLength(255)]
      public string ProviderName { get; set; }

      /// <summary>
      /// Required, Max length = 65535
      /// </summary>
      [Required]
      [MaxLength(65535)]
      [StringLength(65535)]
      public string ProviderSecrets { get; set; }

      /// <summary>
      /// Required, Max length = 65535
      /// </summary>
      [Required]
      [MaxLength(65535)]
      [StringLength(65535)]
      public string ProviderData { get; set; }

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

