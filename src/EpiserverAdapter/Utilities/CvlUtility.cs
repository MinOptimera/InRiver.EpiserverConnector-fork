﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using inRiver.EPiServerCommerce.CommerceAdapter.Communication;
using inRiver.EPiServerCommerce.CommerceAdapter.Enums;
using inRiver.EPiServerCommerce.CommerceAdapter.EpiXml;
using inRiver.EPiServerCommerce.CommerceAdapter.Helpers;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace inRiver.EPiServerCommerce.CommerceAdapter.Utilities
{
    public class CvlUtility
    {
        private Configuration CvlUtilConfig { get; set; }

        public CvlUtility(Configuration cvlUtilConfig)
        {
            this.CvlUtilConfig = cvlUtilConfig;
        }

        public void AddCvl(string cvlId, string folderDateTime)
        {
            List<XElement> metafields = new List<XElement>();
            List<FieldType> affectedFieldTypes = BusinessHelper.GetFieldTypesWithCVL(cvlId);

            foreach (FieldType fieldType in affectedFieldTypes)
            {
                if (EpiMappingHelper.SkipField(fieldType, this.CvlUtilConfig))
                {
                    continue;
                }

                XElement metaField = EpiElement.InRiverFieldTypeToMetaField(fieldType, this.CvlUtilConfig);

                if (fieldType.DataType.Equals(DataType.CVL))
                {
                    metaField.Add(EpiMappingHelper.GetDictionaryValues(fieldType, this.CvlUtilConfig));
                }

                if (metafields.Any(
                    mf =>
                    {
                        XElement nameElement = mf.Element("Name");
                        return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, this.CvlUtilConfig));
                    }))
                {
                    XElement existingMetaField =
                        metafields.FirstOrDefault(
                            mf =>
                            {
                                XElement nameElement = mf.Element("Name");
                                return nameElement != null && nameElement.Value.Equals(EpiMappingHelper.GetEPiMetaFieldNameFromField(fieldType, this.CvlUtilConfig));
                            });

                    if (existingMetaField == null)
                    {
                        continue;
                    }

                    var movefields = metaField.Elements("OwnerMetaClass");
                    existingMetaField.Add(movefields);
                }
                else
                {
                    metafields.Add(metaField);
                }
            }

            XElement metaData = new XElement("MetaDataPlusBackup", new XAttribute("version", "1.0"), metafields.ToArray());
            XDocument doc = EpiDocument.CreateDocument(null, metaData, null, this.CvlUtilConfig);

            Entity channelEntity = RemoteManager.DataService.GetEntity(this.CvlUtilConfig.ChannelId, LoadLevel.DataOnly);
            if (channelEntity == null)
            {
                IntegrationLogger.Write(LogLevel.Error, string.Format("Could not find channel {0} for cvl add", this.CvlUtilConfig.ChannelId));
                return;
            }

            string channelIdentifier = ChannelHelper.GetChannelIdentifier(channelEntity);

            string zippedfileName = DocumentFileHelper.SaveAndZipDocument(channelIdentifier, doc, folderDateTime, this.CvlUtilConfig);
            IntegrationLogger.Write(LogLevel.Debug, string.Format("catalog {0} saved", channelIdentifier));

            if (this.CvlUtilConfig.ActivePublicationMode.Equals(PublicationMode.Automatic))
            {
                IntegrationLogger.Write(LogLevel.Debug, "Starting automatic import!");

                if (EpiApi.Import(Path.Combine(this.CvlUtilConfig.PublicationsRootPath, folderDateTime, Configuration.ExportFileName), ChannelHelper.GetChannelGuid(channelEntity, this.CvlUtilConfig), this.CvlUtilConfig))
                {
                    EpiApi.SendHttpPost(this.CvlUtilConfig, Path.Combine(this.CvlUtilConfig.PublicationsRootPath, folderDateTime, zippedfileName));
                }
            }
        }
    }
}