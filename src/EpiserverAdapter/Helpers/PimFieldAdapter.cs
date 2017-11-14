﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public class PimFieldAdapter : IPimFieldAdapter
    {
        private readonly IConfiguration _config;

        public PimFieldAdapter(IConfiguration config)
        {
            _config = config;
        }

        private static List<CVLValue> cvlValues;

        private static List<CVL> cvls;

        public static List<CVLValue> CVLValues
        {
            get => cvlValues ?? (cvlValues = RemoteManager.ModelService.GetAllCVLValues());
            set => cvlValues = value;
        }

        public static List<CVL> CvLs
        {
            get => cvls ?? (cvls = RemoteManager.ModelService.GetAllCVLs());
            set => cvls = value;
        }

        public bool FieldTypeIsMultiLanguage(FieldType fieldType)
        {
            if (fieldType.DataType.Equals(DataType.LocaleString))
            {
                return true;
            }

            if (!fieldType.DataType.Equals(DataType.CVL))
                return false;

            var cvl = RemoteManager.ModelService.GetCVL(fieldType.CVLId);

            return cvl != null && cvl.DataType.Equals(DataType.LocaleString);
        }
        
        public string GetAllowSearch(FieldType fieldType)
        {
            if (fieldType.Settings.ContainsKey("AllowsSearch"))
            {
                return fieldType.Settings["AllowsSearch"];
            }

            return "true";
        }

        public IEnumerable<string> CultureInfosToStringArray(CultureInfo[] cultureInfo)
        {
            return cultureInfo.Select(ci => ci.Name.ToLower()).ToArray();
        }

        public string GetStartDateFromEntity(Entity entity)
        {
            Field startDateField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("startdate"));

            if (startDateField == null || startDateField.IsEmpty())
            {
                return DateTime.UtcNow.ToString("u");
            }

            return ((DateTime)startDateField.Data).ToUniversalTime().ToString("u");
        }

        public string GetEndDateFromEntity(Entity entity)
        {
            Field endDateField = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains("enddate"));

            if (endDateField == null || endDateField.IsEmpty())
            {
                return DateTime.UtcNow.AddYears(100).ToString("u");
            }

            return ((DateTime)endDateField.Data).ToUniversalTime().ToString("u");
        }

        public string FieldIsUseInCompare(FieldType fieldType)
        {
            string value = "False";

            if (fieldType.Settings.ContainsKey("UseInComparing"))
            {
                value = fieldType.Settings["UseInComparing"];
                if (!(value.ToLower().Equals("false") || value.ToLower().Equals("true")))
                {
                    IntegrationLogger.Write(LogLevel.Error, string.Format("Fieldtype with id {0} has invalid UseInComparing setting", fieldType.Id));
                }
            }

            return value;
        }

        public string GetDisplayNameFromEntity(Entity entity, int maxLength)
        {
            Field displayNameField = entity.DisplayName;

            string returnString;

            if (displayNameField == null || displayNameField.IsEmpty())
            {
                returnString = string.Format("[{0}]", entity.Id);
            }
            else if (displayNameField.FieldType.DataType.Equals(DataType.LocaleString))
            {
                LocaleString ls = (LocaleString)displayNameField.Data;
                if (string.IsNullOrEmpty(ls[_config.LanguageMapping[_config.ChannelDefaultLanguage]]))
                {
                    returnString = string.Format("[{0}]", entity.Id);
                }
                else
                {
                    returnString = ls[_config.LanguageMapping[_config.ChannelDefaultLanguage]];
                }
            }
            else
            {
                returnString = displayNameField.Data.ToString();
            }

            if (maxLength > 0)
            {
                int lenght = returnString.Length;
                if (lenght > maxLength)
                {
                    returnString = returnString.Substring(0, maxLength - 1);
                }
            }

            return returnString;
        }
        
        public string GetFieldValue(Entity entity, string fieldName, CultureInfo ci)
        {
            Field field = entity.Fields.FirstOrDefault(f => f.FieldType.Id.ToLower().Contains(fieldName));

            if (field == null || field.IsEmpty())
            {
                return string.Empty;
            }

            if (field.FieldType.DataType.Equals(DataType.LocaleString))
            {
                return _config.ChannelIdPrefix + ((LocaleString)field.Data)[ci];
            }

            return field.Data.ToString();
        }

        public List<XElement> GetCVLValues(Field field)
        {
            var dataElements = new List<XElement>();
            if (field == null || field.IsEmpty())
                return dataElements;

            var cvl = CvLs.FirstOrDefault(c => c.Id.Equals(field.FieldType.CVLId));
            if (cvl == null)
                return dataElements;

            if (cvl.DataType == DataType.LocaleString)
            {
                foreach (var language in _config.LanguageMapping)
                {
                    var dataElement = GetCvlDataElement(field, language.Key);
                    dataElements.Add(dataElement);
                }
            }
            else
            {
                var dataElement = GetCvlDataElement(field, _config.ChannelDefaultLanguage);
                dataElements.Add(dataElement);
            }
                
            return dataElements;
        }

        private XElement GetCvlDataElement(Field field, CultureInfo language)
        {
            var dataElement = new XElement(
                "Data",
                new XAttribute("language", language.Name.ToLower()),
                new XAttribute("value", GetCvlFieldValue(field, language)));

            return dataElement;
        }

        private string GetCvlFieldValue(Field field, CultureInfo language)
        {
            if (_config.ActiveCVLDataMode.Equals(CVLDataMode.Keys) || FieldIsExcludedCatalogEntryMarkets(field))
            {
                return field.Data.ToString();
            }
           
            string[] keys = field.Data.ToString().Split(';');
            var cvlId = field.FieldType.CVLId;

            var returnValues = new List<string>();
                
            foreach (var key in keys)
            {
                var cvlValue = CVLValues.FirstOrDefault(cv => cv.CVLId.Equals(cvlId) && cv.Key.Equals(key));
                if (cvlValue?.Value == null)
                    continue;

                string finalizedValue;

                if (field.FieldType.DataType.Equals(DataType.LocaleString))
                {
                    LocaleString ls = (LocaleString)cvlValue.Value;
                        
                    if (!ls.ContainsCulture(language))
                        return null;

                    var value = ls[language];
                    finalizedValue = GetFinalizedValue(value, key);
                }
                else
                {
                    var value = cvlValue.Value.ToString();
                    finalizedValue = GetFinalizedValue(value, key);
                }

                returnValues.Add(finalizedValue);
            }

            return string.Join(";", returnValues);
        }

        private static bool FieldIsExcludedCatalogEntryMarkets(Field field)
        {
            return field.FieldType.Settings.ContainsKey("EPiMetaFieldName") &&
                   field.FieldType.Settings["EPiMetaFieldName"].Equals("_ExcludedCatalogEntryMarkets");
        }

        private string GetFinalizedValue(string value, string key)
        {
            if (_config.ActiveCVLDataMode.Equals(CVLDataMode.KeysAndValues))
            {
                value = key + Configuration.CVLKeyDelimiter + value;
            }
            return value;
        }

        public string GetFlatFieldData(Field field)
        {
            if (field == null || field.IsEmpty())
            {
                return string.Empty;
            }

            var dataType = field.FieldType.DataType;
            if (dataType == DataType.Boolean)
            {
                return ((bool)field.Data).ToString();
            }
            if (dataType == DataType.DateTime)
            {
                return ((DateTime)field.Data).ToString("O");
            }
            if (dataType == DataType.Double)
            {
                return ((double)field.Data).ToString(CultureInfo.InvariantCulture);
            }
            if (dataType == DataType.File ||
                dataType == DataType.Integer ||
                dataType == DataType.String ||
                dataType == DataType.Xml)
            {
                return field.Data.ToString();
            }

            return string.Empty;
        }

        internal static void CompareAndParseSkuXmls(string oldXml, string newXml, out List<XElement> skusToAdd, out List<XElement> skusToDelete)
        {
            XDocument oldDoc = XDocument.Parse(oldXml);
            XDocument newDoc = XDocument.Parse(newXml);

            List<XElement> oldSkus = oldDoc.Descendants().Elements("SKU").ToList();
            List<XElement> newSkus = newDoc.Descendants().Elements("SKU").ToList();

            List<string> removables = new List<string>();

            foreach (XElement elem in oldSkus)
            {
                XAttribute id = elem.Attribute("id");
                if (newSkus.Exists(e => e.Attribute("id").Value == id.Value))
                {
                    if (!removables.Exists(y => y == id.Value))
                    {
                        removables.Add(id.Value);
                    }
                }
            }

            foreach (string id in removables)
            {
                oldSkus.RemoveAll(e => e.Attribute("id").Value == id);
                newSkus.RemoveAll(e => e.Attribute("id").Value == id);
            }

            skusToAdd = newSkus;
            skusToDelete = oldSkus;
        }
    }
}